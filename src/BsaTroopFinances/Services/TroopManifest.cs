using System.Text.Json;
using System.Text.Json.Nodes;

namespace BsaTroopFinances.Services;

/// <summary>The web app manifest that makes the site installable ("Add to Home Screen"), named
/// for the troop.</summary>
public static class TroopManifest
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string Json(TroopInfo troop)
    {
        static JsonObject Icon(string src, string sizes, string? purpose = null)
        {
            var icon = new JsonObject { ["src"] = src, ["sizes"] = sizes, ["type"] = "image/png" };
            if (purpose is not null)
            {
                icon["purpose"] = purpose;
            }

            return icon;
        }

        var manifest = new JsonObject
        {
            ["name"] = troop.ShortName,
            ["short_name"] = troop.ShortName,
            ["description"] = $"Reimbursements, scout accounts and fundraiser tracking for {troop.Name}.",
            ["id"] = "/",
            ["start_url"] = "/?source=pwa",
            ["scope"] = "/",
            ["display"] = "standalone",
            ["orientation"] = "any",
            ["theme_color"] = "#003f87",
            ["background_color"] = "#f4f6fb",
            ["icons"] = new JsonArray(
                Icon("icons/icon-192.png", "192x192", "any"),
                Icon("icons/icon-512.png", "512x512", "any"),
                Icon("icons/icon-maskable-512.png", "512x512", "maskable")),
            ["shortcuts"] = new JsonArray(
                new JsonObject { ["name"] = "Submit a Receipt", ["url"] = "/portal/receipts/new", ["icons"] = new JsonArray(Icon("icons/icon-192.png", "192x192")) },
                new JsonObject { ["name"] = "My Scouts", ["url"] = "/portal", ["icons"] = new JsonArray(Icon("icons/icon-192.png", "192x192")) }),
        };
        return manifest.ToJsonString(Indented);
    }
}
