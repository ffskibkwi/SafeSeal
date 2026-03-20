namespace SafeSeal.Core;

public static class TemplatePresetCatalog
{
    public static IReadOnlyList<TemplateDefinition2> GetBuiltInPresets()
    {
        return
        [
            new TemplateDefinition2(
                "visa-passport",
                "Visa Passport",
                "visa",
                1,
                "VISA APPLICATION\\n{{applicant.name}}\\n{{passport.no}}\\n{{date}}",
                [
                    new TemplateVariableDefinition("applicant.name", TemplateValueType.String, true, Min: 1, Max: 80),
                    new TemplateVariableDefinition("passport.no", TemplateValueType.String, true, Min: 5, Max: 20),
                    new TemplateVariableDefinition("date", TemplateValueType.Date, true),
                ]),
            new TemplateDefinition2(
                "legal-reviewed",
                "Legal Reviewed",
                "legal",
                1,
                "LEGAL REVIEW ONLY\\n{{case.id}}\\n{{recipient}}\\n{{date}}",
                [
                    new TemplateVariableDefinition("case.id", TemplateValueType.String, true, Min: 2, Max: 40),
                    new TemplateVariableDefinition("recipient", TemplateValueType.String, true, Min: 2, Max: 80),
                    new TemplateVariableDefinition("date", TemplateValueType.Date, true),
                ]),
            new TemplateDefinition2(
                "research-draft",
                "Research Draft",
                "research",
                1,
                "RESEARCH DRAFT\\n{{project}}\\n{{owner}}\\n{{date}}",
                [
                    new TemplateVariableDefinition("project", TemplateValueType.String, true, Min: 2, Max: 80),
                    new TemplateVariableDefinition("owner", TemplateValueType.String, true, Min: 2, Max: 80),
                    new TemplateVariableDefinition("date", TemplateValueType.Date, true),
                ]),
            new TemplateDefinition2(
                "general-standard",
                "General Standard",
                "general",
                1,
                "FOR {{purpose}} - {{date}}",
                [
                    new TemplateVariableDefinition("purpose", TemplateValueType.String, true, Min: 2, Max: 80),
                    new TemplateVariableDefinition("date", TemplateValueType.Date, true),
                ]),
            new TemplateDefinition2(
                "cn-verification",
                "Verification (CN)",
                "general",
                1,
                "本复印件仅供办理 {{Purpose}} 使用\\n用于 {{Department}} 审核，他用无效",
                [
                    new TemplateVariableDefinition("Purpose", TemplateValueType.String, true, Min: 1, Max: 80),
                    new TemplateVariableDefinition("Department", TemplateValueType.String, true, Min: 1, Max: 80),
                ]),
            new TemplateDefinition2(
                "cn-restricted-use",
                "Restricted Use (CN)",
                "general",
                1,
                "仅供 {{Recipient}} 办理 {{Task}} 使用\\n他用无效",
                [
                    new TemplateVariableDefinition("Recipient", TemplateValueType.String, true, Min: 1, Max: 80),
                    new TemplateVariableDefinition("Task", TemplateValueType.String, true, Min: 1, Max: 80),
                ]),
        ];
    }
}
