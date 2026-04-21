using System.ComponentModel;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class InteractionTools
{
	[McpServerTool(Name = "maui_tap"), Description("Tap a UI element by its visual tree ID. Use maui_tree to discover element IDs.")]
	public static async Task<string> Tap(
		McpAgentSession session,
		[Description("Element ID from the visual tree")] string elementId,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.TapAsync(elementId);
		return success
			? $"Tapped element '{elementId}' successfully."
			: $"Failed to tap element '{elementId}'. Element may not exist or is not tappable.";
	}

	[McpServerTool(Name = "maui_fill"), Description("Fill text into an Entry, Editor, or SearchBar element. Replaces existing text.")]
	public static async Task<string> Fill(
		McpAgentSession session,
		[Description("Element ID from the visual tree")] string elementId,
		[Description("Text to fill into the element")] string text,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.FillAsync(elementId, text);
		return success
			? $"Filled element '{elementId}' with text."
			: $"Failed to fill element '{elementId}'. Element may not exist or is not a text input.";
	}

	[McpServerTool(Name = "maui_clear"), Description("Clear text from an Entry, Editor, or SearchBar element.")]
	public static async Task<string> Clear(
		McpAgentSession session,
		[Description("Element ID from the visual tree")] string elementId,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.ClearAsync(elementId);
		return success
			? $"Cleared element '{elementId}' successfully."
			: $"Failed to clear element '{elementId}'.";
	}

	[McpServerTool(Name = "maui_key"), Description("Send a key press to an element or the app. Supported keys for Entry/Editor/SearchBar: 'enter' (submit or newline), 'backspace' (delete last character). Use 'text' parameter to type characters. Other keys may have no effect depending on the element type.")]
	public static async Task<string> Key(
		McpAgentSession session,
		[Description("Key to press: 'enter', 'return', 'backspace', 'delete'")] string key,
		[Description("Target element ID (optional — uses focused element if omitted)")] string? elementId = null,
		[Description("Text to type character by character into the element")] string? text = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.KeyAsync(key, elementId, text);
		return success
			? elementId is not null ? $"Sent key '{key}' to element '{elementId}'." : $"Sent key '{key}'."
			: $"Failed to send key '{key}'. Element may not support keyboard input.";
	}

	[McpServerTool(Name = "maui_gesture"), Description("Perform a touch gesture on the app. Supported gesture types: 'swipe' (requires direction), 'tap', 'longpress'. Use maui_tap for simple taps — this tool is for advanced gestures like swiping.")]
	public static async Task<string> Gesture(
		McpAgentSession session,
		[Description("Gesture type: 'swipe', 'tap', or 'longpress'")] string type,
		[Description("Target element ID (optional)")] string? elementId = null,
		[Description("Swipe direction: 'up', 'down', 'left', 'right' (required for swipe)")] string? direction = null,
		[Description("Swipe distance in pixels (optional, uses default if omitted)")] double? distance = null,
		[Description("Gesture duration in milliseconds (optional)")] int? durationMs = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var validTypes = new[] { "swipe", "tap", "longpress" };
		if (!validTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
			return $"Unsupported gesture type '{type}'. Supported types: {string.Join(", ", validTypes)}.";

		if (type.Equals("swipe", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(direction))
			return "Swipe gesture requires a 'direction' parameter ('up', 'down', 'left', 'right').";

		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.GestureAsync(type, elementId, direction, distance, durationMs);
		return success
			? elementId is not null ? $"Performed {type} gesture on element '{elementId}'." : $"Performed {type} gesture."
			: $"Failed to perform {type} gesture.";
	}

	[McpServerTool(Name = "maui_scroll"), Description("Scroll a ScrollView, CollectionView, or ListView. Supports delta-based scrolling, scrolling to an item index, or scrolling an element into view.")]
	public static async Task<string> Scroll(
		McpAgentSession session,
		[Description("Element ID of the scroll container, or element to scroll into view")] string? elementId = null,
		[Description("Horizontal scroll delta in pixels")] double? x = null,
		[Description("Vertical scroll delta in pixels")] double? y = null,
		[Description("Whether to animate the scroll (default: true)")] bool? animated = null,
		[Description("Window index for multi-window apps")] int? window = null,
		[Description("Item index to scroll to (for CollectionView/ListView)")] int? itemIndex = null,
		[Description("Group index for grouped CollectionView")] int? groupIndex = null,
		[Description("Scroll position: MakeVisible (default), Start, Center, End")] string? scrollToPosition = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.ScrollAsync(elementId, x ?? 0, y ?? 0, animated ?? true, window, itemIndex, groupIndex, scrollToPosition);
		return success
			? elementId is not null ? $"Scrolled element '{elementId}' successfully." : "Scrolled successfully."
			: $"Failed to scroll element '{elementId}'. Element may not be a ScrollView.";
	}
}
