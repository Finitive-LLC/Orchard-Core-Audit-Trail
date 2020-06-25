using OrchardCore.Modules.Manifest;
using static OrchardCore.AuditTrail.Constants.FeatureIds;

[assembly: Module(
    Name = "AuditTrail",
    Author = "Finitive",
    Version = "1.0",
    Category = "Security"
)]

[assembly: Feature(
    Id = OrchardCore_AuditTrail,
    Name = "AuditTrail",
    Category = "Security",
    Description = "Provides a log for recording and viewing back-end changes.",
    Dependencies = new[]
    {
        "OrchardCore.ContentManagement.Display",
        "OrchardCore.ContentManagement",
        "OrchardCore.ContentTypes",
        "OrchardCore.DisplayManagement",
        "OrchardCore.ResourceManagement",
        "OrchardCore.Users"
    }
)]
