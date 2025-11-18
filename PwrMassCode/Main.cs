using System.Windows.Controls;
using Community.PowerToys.Run.Plugin.PwrMassCode.Properties;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using Wox.Infrastructure;
using Wox.Plugin;
using Wox.Plugin.Logger;
using System.Windows;
using System.Runtime.InteropServices; // for SendInput

namespace Community.PowerToys.Run.Plugin.PwrMassCode
{
    public class Main : IPlugin, IPluginI18n, IContextMenu, ISettingProvider, IReloadable, IDisposable, IDelayedExecutionPlugin
    {
        private const string CopySnippetSetting = nameof(CopySnippetSetting); // copy/paste behavior
        private const string BaseUrlSetting = nameof(BaseUrlSetting); // MassCode base URL
        private const string TitlePrefixSetting = nameof(TitlePrefixSetting);
        private const string TextPrefixSetting = nameof(TextPrefixSetting);
        private const string FolderPrefixSetting = nameof(FolderPrefixSetting);
        private const string TagPrefixSetting = nameof(TagPrefixSetting);
        private const string ExcludeFavoritesSetting = nameof(ExcludeFavoritesSetting); // exclude favorited snippets

        private bool _copySnippet; // when true -> copy, when false (default) -> paste
        private string _baseUrl = "http://localhost:4321"; // default URL
        private PluginInitContext _context;
        private string _iconPath;
        private bool _disposed;
        private string? _lastError;
        public string Name => Resources.plugin_name;
        public string Description => Resources.plugin_description;
        public static string PluginID => "2ed6a07180bc408ab0a881ef73124935";

        private MassCodeClient? _client;
        private IReadOnlyList<Snippet> _cache = Array.Empty<Snippet>();
        private DateTime _cacheAt = DateTime.MinValue;
        private readonly TimeSpan _cacheTtl = TimeSpan.FromSeconds(10);

        // user-configurable prefix characters (defaults)
        private char _prefixTitle = '!' ;
        private char _prefixText = '#' ;
        private char _prefixFolder = '%' ;
        private char _prefixTag = '|';

        // new option: exclude favorited snippets during fetch
        private bool _excludeFavorites = false;

        // TODO: add additional options (optional)
        public IEnumerable<PluginAdditionalOption> AdditionalOptions => new List<PluginAdditionalOption>()
        {
            new PluginAdditionalOption()
            {
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Checkbox,
                Key = CopySnippetSetting,
                DisplayLabel = "Copy snippet",
                Value = false, // default false -> paste
            },
            new PluginAdditionalOption()
            {
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Checkbox,
                Key = ExcludeFavoritesSetting,
                DisplayLabel = "Exclude favorites",
                Value = false,
            },
            new PluginAdditionalOption()
            {
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
                Key = BaseUrlSetting,
                DisplayLabel = "massCode Base URL",
                TextValue = "http://localhost:4321",
            },
            new PluginAdditionalOption()
            {
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
                Key = TitlePrefixSetting,
                DisplayLabel = "Prefix: Title",
                TextValue = " !",
            },
            new PluginAdditionalOption()
            {
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
                Key = TextPrefixSetting,
                DisplayLabel = "Prefix: Text",
                TextValue = "#",
            },
            new PluginAdditionalOption()
            {
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
                Key = FolderPrefixSetting,
                DisplayLabel = "Prefix: Folder",
                TextValue = "%",
            },
            new PluginAdditionalOption()
            {
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
                Key = TagPrefixSetting,
                DisplayLabel = "Prefix: Tags",
                TextValue = "|",
            }
        };

        public void UpdateSettings(PowerLauncherPluginSettings settings)
        {
            // load options
            _copySnippet = settings?.AdditionalOptions?.FirstOrDefault(x => x.Key == CopySnippetSetting)?.Value ?? false;
            var url = settings?.AdditionalOptions?.FirstOrDefault(x => x.Key == BaseUrlSetting)?.TextValue;
            var newBase = string.IsNullOrWhiteSpace(url) ? "http://localhost:4321" : url!.Trim();
            if (!string.Equals(_baseUrl, newBase, StringComparison.Ordinal))
            {
                _baseUrl = newBase;
                _client = MassCodeClient.Create(_baseUrl);
                _cache = Array.Empty<Snippet>();
                _cacheAt = DateTime.MinValue;
            }

            // load customizable prefixes (fallback to defaults on invalid)
            _prefixTitle = ParsePrefix(settings, TitlePrefixSetting, '!' );
            _prefixText = ParsePrefix(settings, TextPrefixSetting, '#');
            _prefixFolder = ParsePrefix(settings, FolderPrefixSetting, '%');
            _prefixTag = ParsePrefix(settings, TagPrefixSetting, '|');

            // load exclude favorites option
            var newExcludeFavorites = settings?.AdditionalOptions?.FirstOrDefault(x => x.Key == ExcludeFavoritesSetting)?.Value ?? false;
            if (newExcludeFavorites != _excludeFavorites)
            {
                _excludeFavorites = newExcludeFavorites;
                _cache = Array.Empty<Snippet>();
                _cacheAt = DateTime.MinValue;
            }
        }

        private static char ParsePrefix(PowerLauncherPluginSettings? settings, string key, char fallback)
        {
            try
            {
                var s = settings?.AdditionalOptions?.FirstOrDefault(x => x.Key == key)?.TextValue;
                if (string.IsNullOrWhiteSpace(s)) return fallback;
                foreach (var ch in s.Trim())
                {
                    if (!char.IsWhiteSpace(ch)) return ch;
                }
                return fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private void EnsureClient()
        {
            if (_client is null)
            {
                _client = MassCodeClient.Create(_baseUrl);
            }
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

        // Win32 SendInput definitions
        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public int type; //1 = keyboard
            public INPUTUNION U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private const int INPUT_KEYBOARD =1;
        private const uint KEYEVENTF_KEYUP =0x0002;
        private const ushort VK_CONTROL =0x11;
        private const ushort V_KEY =0x56;

        private static bool TrySendCtrlV()
        {
            try
            {
                var inputs = new List<INPUT>
                {
                    new INPUT { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_CONTROL } } }, // Ctrl down
                    new INPUT { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = V_KEY } } }, // V down
                    new INPUT { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = V_KEY, dwFlags = KEYEVENTF_KEYUP } } }, // V up
                    new INPUT { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = KEYEVENTF_KEYUP } } }, // Ctrl up
                };
                var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
                if (sent != inputs.Count)
                {
                    var err = Marshal.GetLastWin32Error();
                    Log.Error($"SendInput returned {sent}/{inputs.Count}, Win32Error={err}", typeof(Main));
                }
                return sent == inputs.Count;
            }
            catch (Exception ex)
            {
                Log.Error($"SendInput failed: {ex}", typeof(Main));
                return false;
            }
        }

        private bool ExecuteSnippet(string text) // unified execution based on setting
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (_copySnippet)
            {
                return ClipboardTrySetText(text);
            }
            // paste mode (default)
            var copied = ClipboardTrySetText(text);
            if (!copied) return false;
            try
            {
                // Schedule paste shortly after so it targets the previously active window.
                _ = Task.Run(() =>
                {
                    var ok = TrySendCtrlVWithRetries();
                    if (!ok)
                    {
                        Log.Error("All paste attempts failed", typeof(Main));
                    }
                });
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Paste failed: {ex}", typeof(Main));
                return false;
            }
        }

        private async Task EnsureCacheAsync()
        {
            try
            {
                EnsureClient();
                if (_cache.Count ==0 || (DateTime.UtcNow - _cacheAt) > _cacheTtl)
                {
                    var data = await _client!.GetSnippetsAsync(_excludeFavorites, CancellationToken.None).ConfigureAwait(false);
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

        private static bool ContainsIgnoreCase(string? haystack, string needle)
        {
            if (string.IsNullOrEmpty(needle)) return true;
            if (string.IsNullOrEmpty(haystack)) return false;
            return haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class AdvancedQuery
        {
            public List<string> TitleTerms { get; } = new(); // !
            public List<string> TextTerms { get; } = new(); // #
            public List<string> FolderTerms { get; } = new(); // %
            public List<string> TagTerms { get; } = new(); // |
            public List<string> GenericTerms { get; } = new(); // no prefix
        }

        private AdvancedQuery ParseAdvancedQuery(string search)
        {
            var aq = new AdvancedQuery();
            if (string.IsNullOrWhiteSpace(search)) return aq;
            var tokens = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var raw in tokens)
            {
                if (raw.Length ==0) continue;
                char prefix = raw[0];
                string term = raw;
                if (prefix == _prefixTitle || prefix == _prefixText || prefix == _prefixFolder || prefix == _prefixTag)
                {
                    term = raw.Substring(1);
                    if (string.IsNullOrWhiteSpace(term)) continue;
                    if (prefix == _prefixTitle) aq.TitleTerms.Add(term);
                    else if (prefix == _prefixText) aq.TextTerms.Add(term);
                    else if (prefix == _prefixFolder) aq.FolderTerms.Add(term);
                    else if (prefix == _prefixTag) aq.TagTerms.Add(term);
                }
                else
                {
                    aq.GenericTerms.Add(raw);
                }
            }
            return aq;
        }

        private static bool MatchesAdvanced((Snippet snippet, Content content) t, AdvancedQuery aq)
        {
            // AND across each bucket; buckets empty => ignore
            if (aq.TitleTerms.Count >0 && !aq.TitleTerms.All(tt => ContainsIgnoreCase(t.snippet.Name, tt))) return false;
            if (aq.FolderTerms.Count >0 && !aq.FolderTerms.All(ft => ContainsIgnoreCase(t.snippet.Folder?.Name, ft))) return false;
            if (aq.TagTerms.Count >0)
            {
                if (t.snippet.Tags is null || t.snippet.Tags.Count ==0) return false;
                if (!aq.TagTerms.All(tagTerm => t.snippet.Tags.Any(tag => ContainsIgnoreCase(tag.Name, tagTerm)))) return false;
            }
            if (aq.TextTerms.Count >0 && !aq.TextTerms.All(tt => ContainsIgnoreCase(t.content.Value, tt))) return false;

            if (aq.GenericTerms.Count >0)
            {
                bool anyGeneric = aq.GenericTerms.All(g =>
                     ContainsIgnoreCase(t.snippet.Name, g)
                  || ContainsIgnoreCase(t.content.Label, g)
                  || ContainsIgnoreCase(t.content.Language, g)
                  || ContainsIgnoreCase(t.snippet.Folder?.Name, g)
                  || ContainsIgnoreCase(t.content.Value, g)
                  || (t.snippet.Tags != null && t.snippet.Tags.Any(tag => ContainsIgnoreCase(tag.Name, g)))
                );
                if (!anyGeneric) return false;
            }

            return true;
        }

        private List<Result> BuildResults(string search)
        {
            var list = new List<Result>();
            var flat = Flatten(_cache);

            var aq = ParseAdvancedQuery(search);
            flat = flat.Where(t => MatchesAdvanced(t, aq));

            foreach (var (s, c) in flat.Take(100))
            {
                var sub = $"{c.Label} • {c.Language} — {s.Folder?.Name ?? "Inbox"}";
                var text = c.Value ?? string.Empty;
                list.Add(new Result
                {
                    Title = s.Name ?? string.Empty,
                    SubTitle = sub,
                    IcoPath = _iconPath,
                    QueryTextDisplay = search,
                    Action = _ => ExecuteSnippet(text)
                });
            }

            return list;
        }

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
                var items = BuildResults(query.Search);
                if (items.Count ==0)
                {
                    if (_cache.Count ==0)
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
                            QueryTextDisplay = query.Search,
                        });
                    }
                    else
                    {
                        results.Add(new Result
                        {
                            Title = "No matching snippets",
                            SubTitle = $"Try a broader term. Snippets loaded: {_cache.Count}.",
                            IcoPath = _iconPath,
                            QueryTextDisplay = query.Search,
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
                    QueryTextDisplay = query.Search,
                });
            }

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

            // Ensure cache is ready (first run) so we can show results right away
            try
            {
                EnsureCacheAsync().GetAwaiter().GetResult();
            }
            catch { /* already logged in EnsureCacheAsync */ }

            var items = BuildResults(query.Search);
            if (items.Count ==0)
            {
                if (_cache.Count ==0)
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
                        QueryTextDisplay = query.Search,
                    });
                }
                else
                {
                    results.Add(new Result
                    {
                        Title = "No matching snippets",
                        SubTitle = $"Try a broader term. Snippets loaded: {_cache.Count}.",
                        IcoPath = _iconPath,
                        QueryTextDisplay = query.Search,
                    });
                }
            }
            else
            {
                results.AddRange(items);
            }

            return results;
        }

        private static bool TrySendCtrlVWithRetries(int attempts = 3, int initialDelayMs = 10, int retryDelayMs = 250)
        {
            for (int i =0; i < attempts; i++)
            {
                try
                {
                    if (i ==0)
                    {
                        Thread.Sleep(initialDelayMs);
                    }
                    else
                    {
                        Thread.Sleep(retryDelayMs);
                    }

                    if (TrySendCtrlV()) return true;
                }
                catch (Exception ex)
                {
                    Log.Error($"Paste attempt {i +1} failed: {ex}", typeof(Main));
                }
            }
            return false;
        }

        public void Init(PluginInitContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _context.API.ThemeChanged += OnThemeChanged;
            UpdateIconPath(_context.API.GetCurrentTheme());
            // Ensure client with default URL on init
            EnsureClient();
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
