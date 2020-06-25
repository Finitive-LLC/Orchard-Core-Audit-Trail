using Microsoft.Extensions.Localization;
using OrchardCore.AuditTrail.Models;
using OrchardCore.AuditTrail.Services.Models;
using OrchardCore.Entities;
using System.Collections.Generic;

namespace OrchardCore.AuditTrail.Providers
{
    public class UserAuditTrailEventProvider : AuditTrailEventProviderBase
    {
        public const string SignedUp = nameof(SignedUp);
        public const string LoggedIn = nameof(LoggedIn);
        public const string LogInFailed = nameof(LogInFailed);
        public const string PasswordReset = nameof(PasswordReset);
        public const string PasswordRecovered = nameof(PasswordRecovered);
        public const string Enabled = nameof(Enabled);
        public const string Disabled = nameof(Disabled);
        public const string Created = nameof(Created);


        public UserAuditTrailEventProvider(IStringLocalizer<UserAuditTrailEventProvider> stringLocalizer)
        {
            T = stringLocalizer;
        }


        public override void Describe(DescribeContext context) =>
            context.For<UserAuditTrailEventProvider>("User", T["User"])
                .Event(SignedUp, T["Signed up"], T["A user was successfully signed up."], BuildAuditTrailEvent, true)
                .Event(LoggedIn, T["Logged in"], T["A user was successfully logged in."], BuildAuditTrailEvent, true)
                .Event(LogInFailed, T["Login failed"], T["An attempt to login failed due to incorrect credentials."], BuildAuditTrailEvent, true)
                .Event(PasswordReset, T["Password reset"], T["A user was successfully reset the password."], BuildAuditTrailEvent, true)
                .Event(PasswordRecovered, T["Password recovered"], T["A user was successfully recovered the password."], BuildAuditTrailEvent, true)
                .Event(Enabled, T["Enabled"], T["A user was enabled."], BuildAuditTrailEvent, true)
                .Event(Disabled, T["Disabled"], T["A user was disabled."], BuildAuditTrailEvent, true)
                .Event(Created, T["Created"], T["A user was created."], BuildAuditTrailEvent, true);


        private void BuildAuditTrailEvent(AuditTrailEvent auditTrailEvent, Dictionary<string, object> eventData) =>
            auditTrailEvent.Put(auditTrailEvent.EventName, eventData);
    }
}
