/// <summary>
/// Recognised by BepInEx ConfigurationManager through reflection on the type name and field
/// names; only the fields this mod uses are declared. No reference to any ConfigurationManager
/// build is needed and nothing breaks if none is installed.
/// </summary>
#pragma warning disable 0169, 0414, 0649
internal sealed class ConfigurationManagerAttributes
{
    public bool? IsAdvanced;
    public int? Order;
    public bool? Browsable;
}
