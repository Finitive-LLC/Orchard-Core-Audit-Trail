using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OrchardCore.AuditTrail.Extensions;
using OrchardCore.AuditTrail.Indexes;
using OrchardCore.AuditTrail.Models;
using OrchardCore.AuditTrail.Providers;
using OrchardCore.AuditTrail.Services.Models;
using OrchardCore.AuditTrail.Settings;
using OrchardCore.Entities;
using OrchardCore.Modules;
using OrchardCore.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using YesSql;
using IYesSqlSession = YesSql.ISession;

namespace OrchardCore.AuditTrail.Services
{
    public class AuditTrailManager : IAuditTrailManager
    {
        private readonly IClock _clock;
        private readonly IStringLocalizer T;
        private readonly IYesSqlSession _session;
        private readonly IHttpContextAccessor _hca;
        private readonly ISiteService _siteService;
        private readonly Entities.IIdGenerator _iidGenerator;
        private readonly IEnumerable<IAuditTrailEventHandler> _auditTrailEventHandlers;
        private readonly IEnumerable<IAuditTrailEventProvider> _auditTrailEventProviders;

        public ILogger Logger { get; set; }


        public AuditTrailManager(
            IClock clock,
            IYesSqlSession session,
            ISiteService siteService,
            IHttpContextAccessor hca,
            ILogger<AuditTrailManager> logger,
            Entities.IIdGenerator iidGenerator,
            IStringLocalizer<AuditTrailManager> stringLocalizer,
            IEnumerable<IAuditTrailEventHandler> auditTrailEventHandlers,
            IEnumerable<IAuditTrailEventProvider> auditTrailEventProviders)
        {
            _hca = hca;
            _clock = clock;
            _session = session;
            _siteService = siteService;
            _iidGenerator = iidGenerator;
            _auditTrailEventHandlers = auditTrailEventHandlers;
            _auditTrailEventProviders = auditTrailEventProviders;

            Logger = logger;
            T = stringLocalizer;
        }


        public async Task AddAuditTrailEventAsync<TAuditTrailEventProvider>(AuditTrailContext auditTrailContext)
            where TAuditTrailEventProvider : IAuditTrailEventProvider
        {
            var eventDescriptors = DescribeEvents(auditTrailContext.EventName, typeof(TAuditTrailEventProvider).FullName);

            foreach (var eventDescriptor in eventDescriptors)
            {
                if (!await IsEventEnabledAsync(eventDescriptor)) return;

                var auditTrailCreateContext = new AuditTrailCreateContext(
                    auditTrailContext.EventName,
                    auditTrailContext.UserName,
                    auditTrailContext.EventData,
                    auditTrailContext.EventFilterKey,
                    auditTrailContext.EventFilterData);

                _auditTrailEventHandlers.Invoke((handler, context)
                    => handler.CreateAsync(context), auditTrailCreateContext, Logger);
                
                var auditTrailEvent = new AuditTrailEvent
                {
                    Id = _iidGenerator.GenerateUniqueId(),
                    Category = eventDescriptor.CategoryDescriptor.Category,
                    EventName = auditTrailCreateContext.EventName,
                    FullEventName = eventDescriptor.FullEventName,
                    UserName = !string.IsNullOrEmpty(auditTrailCreateContext.UserName) ? auditTrailContext.UserName : T["[empty]"],
                    CreatedUtc = auditTrailCreateContext.CreatedUtc ?? _clock.UtcNow,
                    Comment = auditTrailCreateContext.Comment.NewlinesToHtml(),
                    EventFilterData = auditTrailCreateContext.EventFilterData,
                    EventFilterKey = auditTrailCreateContext.EventFilterKey,
                    ClientIpAddress = string.IsNullOrEmpty(auditTrailCreateContext.ClientIpAddress) ?
                        await GetClientAddressAsync() : auditTrailCreateContext.ClientIpAddress
                };

                eventDescriptor.BuildAuditTrailEvent(auditTrailEvent, auditTrailCreateContext.EventData);

                _session.Save(auditTrailEvent);
            }
        }

        public async Task<AuditTrailEventSearchResults> GetAuditTrailEventsAsync(
            int page,
            int pageSize,
            Filters filters = null,
            AuditTrailOrderBy orderBy = AuditTrailOrderBy.DateDescending)
        {
            var session = _session.Store.CreateSession(System.Data.IsolationLevel.ReadUncommitted);
            var query = session.Query<AuditTrailEvent>();
            
            if (filters != null)
            {
                var filterContext = new QueryFilterContext(query, filters);

                _auditTrailEventHandlers.Invoke((handler, context) => handler.Filter(context), filterContext, Logger);

                // Give each provider a chance to modify the query.
                var providersContext = DescribeProviders();
                foreach (var queryFilter in providersContext.QueryFilters)
                {
                    queryFilter(filterContext);
                }

                query = filterContext.Query;
            }

            switch (orderBy)
            {
                case AuditTrailOrderBy.CategoryAscending:
                    query.With<AuditTrailEventIndex>().OrderBy(eventIndex => eventIndex.Category).ThenByDescending(eventIndex => eventIndex.Id);
                    break;
                case AuditTrailOrderBy.EventAscending:
                    query.With<AuditTrailEventIndex>().OrderBy(eventIndex => eventIndex.EventName).ThenByDescending(eventIndex => eventIndex.Id);
                    break;
                case AuditTrailOrderBy.DateDescending:
                    query.With<AuditTrailEventIndex>().OrderByDescending(eventIndex => eventIndex.CreatedUtc).ThenByDescending(eventIndex => eventIndex.Id);
                    break;
            }

            var auditTrailEvents = await query.ListAsync();
            var auditTrailEventsTotalCount = auditTrailEvents.Count();

            var startIndex = (page - 1) * pageSize;
            auditTrailEvents = auditTrailEvents.Skip(startIndex);

            if (pageSize > 0)
            {
                auditTrailEvents = auditTrailEvents.Take(pageSize);
            }

            return new AuditTrailEventSearchResults
            {
                AuditTrailEvents = auditTrailEvents,
                TotalCount = auditTrailEventsTotalCount
            };
        }

        public async Task<int> TrimAsync(TimeSpan retentionPeriod)
        {
            var dateThreshold = _clock.UtcNow.AddDays(1) - retentionPeriod;
            var auditTrailEvents = await _session.Query<AuditTrailEvent, AuditTrailEventIndex>()
                .Where(index => index.CreatedUtc <= dateThreshold).ListAsync();

            var deletedEvents = 0;

            // Related Orchard Core issue to be able to delete items without a foreach:
            // https://github.com/OrchardCMS/OrchardCore/issues/5821
            foreach (var auditTrailEvent in auditTrailEvents)
            {
                _session.Delete(auditTrailEvent);
                deletedEvents++;
            }

            return deletedEvents;
        }

        public async Task<AuditTrailEvent> GetAuditTrailEventAsync(string auditTrailEventId) =>
            await _session.Query<AuditTrailEvent, AuditTrailEventIndex>()
                .Where(x => x.AuditTrailEventId == auditTrailEventId).FirstOrDefaultAsync();

        public IEnumerable<AuditTrailCategoryDescriptor> DescribeCategories() =>
            DescribeProviders().Describe();

        public DescribeContext DescribeProviders()
        {
            var describeContext = new DescribeContext();
            _auditTrailEventProviders.Invoke((provider, context) => provider.Describe(context), describeContext, Logger);
            return describeContext;
        }

        public AuditTrailEventDescriptor DescribeEvent(AuditTrailEvent auditTrailEvent) =>
            DescribeCategories().SelectMany(
                categoryDescriptor => categoryDescriptor.Events.Where(
                    eventDescriptor => eventDescriptor.FullEventName == auditTrailEvent.FullEventName)).FirstOrDefault();


        private IEnumerable<AuditTrailEventDescriptor> DescribeEvents(string eventName, string providerName) =>
            DescribeCategories().Where(
                categoryDescriptor => categoryDescriptor.Category == DescribeProviderCategory(providerName))
                    .SelectMany(categoryDescriptor => categoryDescriptor.Events
                        .Where(eventDescriptor => eventDescriptor.EventName == eventName));

        private string DescribeProviderCategory(string providerName) =>
            DescribeProviders().Describe().Where(category => category.ProviderName == providerName)
                .Select(categoryDescriptor => categoryDescriptor.Category).FirstOrDefault();

        private async Task<string> GetClientAddressAsync()
        {
            var settings = await GetAuditTrailSettingsAsync();

            if (!settings.EnableClientIpAddressLogging) return null;

            return _hca.HttpContext.Connection.RemoteIpAddress.ToString();
        }

        private async Task<AuditTrailSettings> GetAuditTrailSettingsAsync() =>
            (await _siteService.GetSiteSettingsAsync()).As<AuditTrailSettings>();

        private async Task<bool> IsEventEnabledAsync(AuditTrailEventDescriptor eventDescriptor)
        {
            if (eventDescriptor.IsMandatory) return true;

            var auditTrailSettings = await GetAuditTrailSettingsAsync();

            var auditTrailEventSetting = auditTrailSettings.EventSettings.FirstOrDefault(
                eventSetting => eventSetting.EventName == eventDescriptor.FullEventName);

            return auditTrailEventSetting != null ? auditTrailEventSetting.IsEnabled : eventDescriptor.IsEnabledByDefault;
        }
    }
}
