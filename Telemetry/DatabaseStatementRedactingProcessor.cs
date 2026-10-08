using System.Diagnostics;
using OpenTelemetry;

namespace Cronus.DeliveryService.Telemetry;

/// <summary>Removes SQL text before database spans are exported.</summary>
/// <remarks>
/// Npgsql populates SQL tags in the driver; redaction must run after the span ends and before export.
/// Keep dependency metadata, but remove both tag names to avoid leaking schema after convention changes.
/// </remarks>
internal sealed class DatabaseStatementRedactingProcessor : BaseProcessor<Activity>
{
    private static readonly string[] StatementTextTags = ["db.query.text", "db.statement"];

    /// <summary>Redacts completed spans before the exporter reads them.</summary>
    /// <remarks>Registered during pipeline construction, before Azure Monitor appends its exporter at host start.</remarks>
    public override void OnEnd(Activity data)
    {
        foreach (var tag in StatementTextTags)
        {

            data.SetTag(tag, null);
        }
    }
}
