﻿using Microsoft.Extensions.Primitives;
using OrchardCore.DisplayManagement.ModelBinding;
using System.Collections.Generic;

namespace OrchardCore.AuditTrail.Services.Models
{
    public class Filters : Dictionary<string, string>
    {
        public IUpdateModel UpdateModel { get; set; }


        public Filters(IUpdateModel updateModel)
        {
            UpdateModel = updateModel;
        }


        public Filters AddFilter(string key, string value)
        {
            Add(key, value);
            return this;
        }


        public static Filters From(Dictionary<string, StringValues> nameValues, IUpdateModel updateModel)
        {
            var filters = new Filters(updateModel);

            foreach (var nameValue in nameValues)
            {
                if (!string.IsNullOrEmpty(nameValue.Value) && !string.IsNullOrEmpty(nameValue.Value))
                {
                    filters.Add(nameValue.Key, nameValue.Value);
                }
            }

            return filters;
        }
    }
}
