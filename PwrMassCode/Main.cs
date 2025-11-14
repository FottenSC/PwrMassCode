using System.Windows.Controls;
using Community.PowerToys.Run.Plugin.PwrMassCode.Properties;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using Wox.Infrastructure;
using Wox.Plugin;
using Wox.Plugin.Logger;
using System.Windows;

namespace Community.PowerToys.Run.Plugin.PwrMassCode
{
    public class Main : IPlugin, IPluginI18n, IContextMenu, ISettingProvider, IReloadable, IDisposable, IDelayedExecutionPlugin
    {
        private const string Setting = nameof(Setting);
        // current value of the setting
        private bool _setting;
        private PluginInitContext _context;
        private string _iconPath;
        private bool _disposed;
        private string? _lastError;
        public string Name => Resources.plugin_name;
        public string Description => Resources.plugin_description;
        public static string PluginID => "2ed6a07180bc408ab0a881ef73124935";

        private readonly MassCodeClient _client = MassCodeClient.Create();
        private IReadOnlyList<Snippet> _cache = Array.Empty<Snippet>();
        private DateTime _cacheAt = DateTime.MinValue;
        private readonly TimeSpan _cacheTtl = TimeSpan.FromSeconds(10);

        // TODO: add additional options (optional)
        public IEnumerable<PluginAdditionalOption> AdditionalOptions => new List<PluginAdditionalOption>()
        {
            new PluginAdditionalOption()
            {
                PluginOptionType= PluginAdditionalOption.AdditionalOptionType.Checkbox,
                Key = Setting,
                DisplayLabel = Resources.plugin_setting,
            },
        };

        public void UpdateSettings(PowerLauncherPluginSettings settings)
        {
            _setting = settings?.AdditionalOptions?.FirstOrDefault(x => x.Key == Setting)?.Value ?? false;
        }

        // TODO: return context menus for each Result (optional)
        public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
        {
            return new List<ContextMenuResult>(0);
        }

        private static bool ClipboardTrySetText(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return false;
                Clipboard.SetDataObject(text, true);
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"Clipboard error: {e.Message}", typeof(Main));
                return false;
            }
        }

        private async Task EnsureCacheAsync()
        {
            try
            {
                if (_cache.Count == 0 || (DateTime.UtcNow - _cacheAt) > _cacheTtl)
                {
                    var data = await _client.GetSnippetsAsync(CancellationToken.None).ConfigureAwait(false);
                    _cache = data;
                    _cacheAt = DateTime.UtcNow;
                    _lastError = null;
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                Log.Error($"massCode API error: {ex}", typeof(Main));
            }
        }

        private static IEnumerable<(Snippet snippet, Content content)> Flatten(IReadOnlyList<Snippet> items)
        {
            foreach (var s in items)
            {
                if (s.IsDeleted) continue;
                foreach (var c in s.Contents)
                {
                    yield return (s, c);
                }
            }
        }

        private List<Result> BuildResults(string search)
        {
            var list = new List<Result>();
            var flat = Flatten(_cache);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                flat = flat.Where(t =>
                (t.snippet.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (t.content.Label?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (t.content.Language?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (t.snippet.Folder?.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (t.content.Value?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                );
            }

            foreach (var (s, c) in flat.Take(100))
            {
                var sub = $"{c.Label} • {c.Language} — {s.Folder?.Name ?? "Inbox"}";
                var text = c.Value ?? string.Empty;
                list.Add(new Result
                {
                    Title = s.Name ?? string.Empty,
                    SubTitle = sub,
                    IcoPath = _iconPath,
                    Action = _ => ClipboardTrySetText(text)
                });
            }

            return list;
        }

        private List<Result> BuildCreateResultIfAny(string search)
        {
            var results = new List<Result>();
            if (string.IsNullOrWhiteSpace(search)) return results;

            var s = search.Trim();
            bool isCreate = s.StartsWith("new ", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("create ", StringComparison.OrdinalIgnoreCase);
            if (!isCreate) return results;

            var name = s[(s.IndexOf(' ') + 1)..].Trim();
            if (string.IsNullOrEmpty(name)) return results;

            var hasText = false;
            var text = string.Empty;
            try
            {
                hasText = Clipboard.ContainsText();
                if (hasText) text = Clipboard.GetText();
            }
            catch (Exception ex)
            {
                Log.Error($"Clipboard read error: {ex}", typeof(Main));
            }

            if (!hasText || string.IsNullOrWhiteSpace(text)) return results;

            results.Add(new Result
            {
                Title = $"Create massCode snippet: {name}",
                SubTitle = "From clipboard (Fragment1 • plain_text)",
                IcoPath = _iconPath,
                Action = _ =>
                {
                    try
                    {
                        var id = _client.CreateSnippetAsync(new CreateSnippetRequest
                        {
                            Name = name,
                            FolderId = null
                        }, CancellationToken.None).GetAwaiter().GetResult();

                        if (id > 0)
                        {
                            _client.CreateContentAsync(id, new CreateContentRequest
                            {
                                Label = "Fragment1",
                                Language = "plain_text",
                                Value = text
                            }, CancellationToken.None).GetAwaiter().GetResult();
                        }
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Create snippet failed: {ex}", typeof(Main));
                        return false;
                    }
                }
            });

            return results;
        }

        // Return query results immediately (also fetch cache here for reliability)
        public List<Result> Query(Query query)
        {
            ArgumentNullException.ThrowIfNull(query);

            var results = new List<Result>();

            // empty query
            if (string.IsNullOrEmpty(query.Search))
            {
                results.Add(new Result
                {
                    Title = Name,
                    SubTitle = Description,
                    QueryTextDisplay = string.Empty,
                    IcoPath = _iconPath,
                    Action = action =>
                    {
                        return true;
                    },
                });
                return results;
            }

            // Create suggestion
            results.AddRange(BuildCreateResultIfAny(query.Search));

            // Ensure cache is ready (first run) so we can show results right away
            try
            {
                EnsureCacheAsync().GetAwaiter().GetResult();
            }
            catch { /* already logged in EnsureCacheAsync */ }

            var items = BuildResults(query.Search);
            if (items.Count == 0)
            {
                if (_cache.Count == 0)
                {
                    var subtitle = "Could not fetch snippets. Ensure app is running (default port4321).";
                    if (!string.IsNullOrWhiteSpace(_lastError))
                    {
                        subtitle += $" Error: {_lastError}";
                    }
                    results.Add(new Result
                    {
                        Title = "massCode connection issue",
                        SubTitle = subtitle,
                        IcoPath = _iconPath,
                    });
                }
                else
                {
                    results.Add(new Result
                    {
                        Title = "No matching snippets",
                        SubTitle = $"Try a broader term. Snippets loaded: {_cache.Count}.",
                        IcoPath = _iconPath,
                    });
                }
            }
            else
            {
                results.AddRange(items);
            }

            return results;
        }

        // Optional delayed query (kept for compatibility)
        public List<Result> Query(Query query, bool delayedExecution)
        {
            ArgumentNullException.ThrowIfNull(query);

            var results = new List<Result>();

            if (string.IsNullOrEmpty(query.Search))
            {
                return results;
            }

            try
            {
                EnsureCacheAsync().GetAwaiter().GetResult();
                results.AddRange(BuildCreateResultIfAny(query.Search));
                var items = BuildResults(query.Search);
                if (items.Count == 0)
                {
                    if (_cache.Count == 0)
                    {
                        var subtitle = "Could not fetch snippets. Ensure app is running (default port4321).";
                        if (!string.IsNullOrWhiteSpace(_lastError))
                        {
                            subtitle += $" Error: {_lastError}";
                        }
                        results.Add(new Result
                        {
                            Title = "massCode connection issue",
                            SubTitle = subtitle,
                            IcoPath = _iconPath,
                        });
                    }
                    else
                    {
                        results.Add(new Result
                        {
                            Title = "No matching snippets",
                            SubTitle = $"Try a broader term. Snippets loaded: {_cache.Count}.",
                            IcoPath = _iconPath,
                        });
                    }
                }
                else
                {
                    results.AddRange(items);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Query failed: {ex}", typeof(Main));
                results.Add(new Result
                {
                    Title = "massCode connection error",
                    SubTitle = $"App not running or port is incorrect. Error: {ex.Message}",
                    IcoPath = _iconPath,
                });
            }

            return results;
        }

        public void Init(PluginInitContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _context.API.ThemeChanged += OnThemeChanged;
            UpdateIconPath(_context.API.GetCurrentTheme());
        }

        public string GetTranslatedPluginTitle()
        {
            return Resources.plugin_name;
        }

        public string GetTranslatedPluginDescription()
        {
            return Resources.plugin_description;
        }

        private void OnThemeChanged(Theme oldTheme, Theme newTheme)
        {
            UpdateIconPath(newTheme);
        }

        private void UpdateIconPath(Theme theme)
        {
            if (theme == Theme.Light || theme == Theme.HighContrastWhite)
            {
                _iconPath = "Images/PwrMassCode.light.png";
            }
            else
            {
                _iconPath = "Images/PwrMassCode.dark.png";
            }
        }

        public Control CreateSettingPanel()
        {
            throw new NotImplementedException();
        }

        public void ReloadData()
        {
            if (_context is null)
            {
                return;
            }

            UpdateIconPath(_context.API.GetCurrentTheme());
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                if (_context != null && _context.API != null)
                {
                    _context.API.ThemeChanged -= OnThemeChanged;
                }

                _disposed = true;
            }
        }
    }
}
