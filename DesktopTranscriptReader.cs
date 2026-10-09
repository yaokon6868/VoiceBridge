using System.Security.Cryptography;
using System.Text;
using System.Windows.Automation;
using System.Globalization;

namespace VoiceBridge;

public enum DesktopSpeaker { None, User, Assistant }
public sealed record DesktopUiNode(string Kind, string Name, DesktopUiNode[] Children);
public sealed record DesktopTranscriptFrame(bool VoiceVisible, bool IdleVisible, DesktopSpeaker Speaker,
    string UserKey, string Text, bool Complete, int RoleCount, int NodeCount);

// Captures one cached accessibility tree; interpretation is pure and testable.
// Only text runs are read. Toolbar/button/editor descendants are excluded.
public static class DesktopTranscriptReader
{
    private static readonly HashSet<string> VoiceButtons = new(StringComparer.OrdinalIgnoreCase)
        { "结束语音聊天", "停止语音聊天", "结束语音", "End voice chat", "Stop voice chat" };
    public static DesktopTranscriptFrame Capture(IntPtr window) => Parse(CaptureTree(window));
    public static DesktopUiNode CaptureTree(IntPtr window)
    {
        var cache = new CacheRequest { TreeScope = TreeScope.Subtree,
            TreeFilter = Automation.RawViewCondition, AutomationElementMode = AutomationElementMode.None };
        cache.Add(AutomationElement.NameProperty); cache.Add(AutomationElement.ControlTypeProperty);
        // A Chromium accessibility tree can change while UIA builds its cache.
        // Retry a torn response once; the watcher retains its cursor on failure.
        for (var attempt = 0; ; attempt++)
        {
            try { return Read(AutomationElement.FromHandle(window).GetUpdatedCache(cache)); }
            catch (IndexOutOfRangeException) when (attempt == 0) { }
            catch (ElementNotAvailableException) when (attempt == 0) { }
        }
    }
    private static DesktopUiNode Read(AutomationElement element)
    {
        var data = element.Cached;
        var children = element.CachedChildren;
        var result = new DesktopUiNode[children?.Count ?? 0];
        for (var i = 0; i < result.Length; i++) result[i] = Read(children![i]);
        return new(data.ControlType.ProgrammaticName, data.Name ?? "", result);
    }
    public static DesktopTranscriptFrame Parse(DesktopUiNode root)
    {
        var voice = false; var idle = false; var collect = false; var complete = false;
        var speaker = DesktopSpeaker.None; var roles = 0; var count = 0;
        var body = new StringBuilder(); var user = new StringBuilder();
        void Visit(DesktopUiNode node, bool interactive = false)
        {
            count++;
            if (node.Kind == "ControlType.Button")
            {
                voice |= VoiceButtons.Contains(node.Name);
                idle |= node.Name is "开启语音聊天" or "Start voice chat";
                interactive = true;
            }
            if (node.Kind == "ControlType.Edit") { collect = false; interactive = true; }
            if (!interactive && node.Kind == "ControlType.Text")
            {
                var name = node.Name;
                if (name is "ChatGPT 说：" or "ChatGPT said:" or "ChatGPT says:")
                { speaker = DesktopSpeaker.Assistant; collect = true; complete = false; body.Clear(); roles++; }
                else if (name is "你说：" or "You said:" or "You say:")
                { speaker = DesktopSpeaker.User; collect = true; complete = false; user.Clear(); body.Clear(); roles++; }
                else if (name is "最新回复" or "最新一条回复" or "Latest response" or "随心输入") collect = false;
                else if (collect && name is "回复已完成" or "Response completed") complete = true;
                else if (collect && name.Length > 0)
                {
                    if (speaker == DesktopSpeaker.Assistant) body.Append(name);
                    else if (speaker == DesktopSpeaker.User) user.Append(name);
                }
                // Accessible text parents can repeat their own content in text
                // children. Consume one text run once, not both representations.
                if (name.Length > 0) return;
            }
            foreach (var child in node.Children)
            {
                // Verified desktop layout: a message's clock is a direct text
                // sibling of its body groups, outside all paragraph containers.
                // Stop at that boundary so elapsed tool/status text cannot change
                // user identity or enter a spoken assistant response. A clock
                // inside a paragraph is ordinary content and remains readable.
                if (collect && node.Kind == "ControlType.Group" && child.Kind == "ControlType.Text"
                    && node.Children.Any(c => c.Kind == "ControlType.Text" && c.Name is
                        "你说：" or "You said:" or "You say:" or "ChatGPT 说：" or "ChatGPT said:" or "ChatGPT says:")
                    && DateTime.TryParseExact(child.Name, "HH:mm", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out _)) collect = false;
                Visit(child, interactive);
            }
        }
        Visit(root);
        var normalized = string.Concat(user.ToString().Where(char.IsLetterOrDigit)).ToLowerInvariant();
        var userKey = normalized.Length == 0 ? "" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return new(voice, idle, speaker, userKey, body.ToString(), complete, roles, count);
    }
}
