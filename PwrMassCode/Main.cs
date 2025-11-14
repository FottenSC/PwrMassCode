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
        private const string Setting = nameof(Setting);
        private const string PasteInsteadOfCopySetting = nameof(PasteInsteadOfCopySetting); // new setting key
        // current value of the setting
        private bool _setting;
        private bool _pasteInsteadOfCopy; // flag for paste behavior
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
            new PluginAdditionalOption() // new option
            {
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Checkbox,
                Key = PasteInsteadOfCopySetting,
                DisplayLabel = "Paste snippet instead of copy",
            }
        };

        public void UpdateSettings(PowerLauncherPluginSettings settings)
        {
            _setting = settings?.AdditionalOptions?.FirstOrDefault(x => x.Key == Setting)?.Value ?? false;
            _pasteInsteadOfCopy = settings?.AdditionalOptions?.FirstOrDefault(x => x.Key == PasteInsteadOfCopySetting)?.Value ?? false; // load new option
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
            if (_pasteInsteadOfCopy == false)
            {
                return ClipboardTrySetText(text);
            }
            // paste mode
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
                if (_cache.Count ==0 || (DateTime.UtcNow - _cacheAt) > _cacheTtl)
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
                    Action = _ => ExecuteSnippet(text) // changed to unified method
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

            var name = s[(s.IndexOf(' ') +1)..].Trim();
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

                        if (id >0)
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

        private static bool TrySendCtrlVWithRetries(int attempts =3, int initialDelayMs =450, int retryDelayMs =250)
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
