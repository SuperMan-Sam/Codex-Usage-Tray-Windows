using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using CodexUsageTray.Core.Models;
using CodexUsageTray.Core.Services;
using CodexUsageTray.Services;
using Microsoft.Windows.Widgets.Providers;

namespace CodexUsageTray.Widgets;

internal sealed class CodexUsageWidgetProvider : IWidgetProvider
{
    public const string DefinitionId = "CodexUsage_Widget";

    private const string TemplateJson = """
        {
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "type": "AdaptiveCard",
          "version": "1.5",
          "body": [
            {
              "type": "TextBlock",
              "text": "Codex Usage",
              "weight": "Bolder",
              "size": "Medium",
              "wrap": true
            },
            {
              "type": "ColumnSet",
              "spacing": "Medium",
              "columns": [
                {
                  "type": "Column",
                  "width": "stretch",
                  "items": [
                    {
                      "type": "TextBlock",
                      "text": "5h limit",
                      "isSubtle": true,
                      "spacing": "None"
                    },
                    {
                      "type": "TextBlock",
                      "text": "${fiveHourPercent}",
                      "size": "ExtraLarge",
                      "weight": "Bolder",
                      "spacing": "Small"
                    },
                    {
                      "type": "TextBlock",
                      "text": "${fiveHourReset}",
                      "isSubtle": true,
                      "spacing": "None"
                    }
                  ]
                },
                {
                  "type": "Column",
                  "width": "stretch",
                  "items": [
                    {
                      "type": "TextBlock",
                      "text": "Weekly limit",
                      "isSubtle": true,
                      "spacing": "None"
                    },
                    {
                      "type": "TextBlock",
                      "text": "${weeklyPercent}",
                      "size": "ExtraLarge",
                      "weight": "Bolder",
                      "spacing": "Small"
                    },
                    {
                      "type": "TextBlock",
                      "text": "${weeklyReset}",
                      "isSubtle": true,
                      "spacing": "None"
                    }
                  ]
                }
              ]
            },
            {
              "type": "TextBlock",
              "text": "${statusText}",
              "isSubtle": true,
              "spacing": "Medium",
              "wrap": true
            },
            {
              "type": "TextBlock",
              "text": "${updatedAt}",
              "isSubtle": true,
              "size": "Small",
              "spacing": "None"
            }
          ],
          "actions": [
            {
              "type": "Action.OpenUrl",
              "title": "Open usage page",
              "url": "https://chatgpt.com/codex/settings/usage"
            }
          ]
        }
        """;

    private static readonly ConcurrentDictionary<string, RunningWidgetInfo> RunningWidgets = new();

    public CodexUsageWidgetProvider()
    {
        TryLoadRunningWidgets();
    }

    public void CreateWidget(WidgetContext widgetContext)
    {
        RunningWidgetInfo info = new(widgetContext.Id, widgetContext.DefinitionId);
        RunningWidgets[widgetContext.Id] = info;
        UpdateWidget(info, UsageSnapshotCache.LoadOrUnknown());
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        RunningWidgets.TryRemove(widgetId, out _);
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        string widgetId = actionInvokedArgs.WidgetContext.Id;
        if (RunningWidgets.TryGetValue(widgetId, out RunningWidgetInfo? info))
        {
            UpdateWidget(info, UsageSnapshotCache.LoadOrUnknown());
        }
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        string widgetId = contextChangedArgs.WidgetContext.Id;
        if (RunningWidgets.TryGetValue(widgetId, out RunningWidgetInfo? info))
        {
            UpdateWidget(info, UsageSnapshotCache.LoadOrUnknown());
        }
    }

    public void Activate(WidgetContext widgetContext)
    {
        RunningWidgetInfo info = RunningWidgets.GetOrAdd(widgetContext.Id, _ => new RunningWidgetInfo(widgetContext.Id, widgetContext.DefinitionId));
        info.IsActive = true;
        UpdateWidget(info, UsageSnapshotCache.LoadOrUnknown());
    }

    public void Deactivate(string widgetId)
    {
        if (RunningWidgets.TryGetValue(widgetId, out RunningWidgetInfo? info))
        {
            info.IsActive = false;
        }
    }

    public static void UpdateAllFromSnapshot(UsageSnapshot snapshot)
    {
        foreach (RunningWidgetInfo info in RunningWidgets.Values)
        {
            UpdateWidget(info, snapshot);
        }
    }

    private static void UpdateWidget(RunningWidgetInfo info, UsageSnapshot snapshot)
    {
        if (!string.Equals(info.DefinitionId, DefinitionId, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            WidgetUpdateRequestOptions options = new(info.WidgetId)
            {
                Template = TemplateJson,
                Data = CreateDataJson(snapshot),
                CustomState = snapshot.CapturedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            };
            WidgetManager.GetDefault().UpdateWidget(options);
        }
        catch
        {
            // Widget APIs throw when the app is running unpackaged or the Widgets host is unavailable.
        }
    }

    private static string CreateDataJson(UsageSnapshot snapshot)
    {
        return JsonSerializer.Serialize(new
        {
            fiveHourPercent = FormatPercent(snapshot.FiveHourLimit),
            fiveHourReset = FormatReset(snapshot.FiveHourLimit, snapshot.CapturedAt),
            weeklyPercent = FormatPercent(snapshot.WeeklyLimit),
            weeklyReset = FormatReset(snapshot.WeeklyLimit, snapshot.CapturedAt),
            statusText = FormatStatus(snapshot),
            updatedAt = $"Updated {snapshot.CapturedAt.ToLocalTime():g}",
        });
    }

    private static string FormatPercent(UsageLimitSnapshot? limit)
    {
        return limit is null ? "--" : $"{limit.PercentRemaining}%";
    }

    private static string FormatReset(UsageLimitSnapshot? limit, DateTimeOffset capturedAt)
    {
        return UsageResetTimeFormatter.FormatHoursUntilReset(limit, capturedAt);
    }

    private static string FormatStatus(UsageSnapshot snapshot)
    {
        return snapshot.Status switch
        {
            UsageStatus.Available => "Usage loaded",
            UsageStatus.LimitReached => "Limit reached",
            UsageStatus.LoginRequired => "Open Codex Usage Tray to sign in",
            UsageStatus.Unsupported => "Login unsupported in embedded browser",
            _ => "Usage unavailable",
        };
    }

    private static void TryLoadRunningWidgets()
    {
        try
        {
            foreach (WidgetInfo widgetInfo in WidgetManager.GetDefault().GetWidgetInfos())
            {
                WidgetContext context = widgetInfo.WidgetContext;
                RunningWidgets.TryAdd(context.Id, new RunningWidgetInfo(context.Id, context.DefinitionId));
            }
        }
        catch
        {
            // The provider can be constructed during normal unpackaged app runs.
        }
    }

    private sealed class RunningWidgetInfo(string widgetId, string definitionId)
    {
        public string WidgetId { get; } = widgetId;

        public string DefinitionId { get; } = definitionId;

        public bool IsActive { get; set; }
    }
}
