using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class JobTools
{
	[McpServerTool(Name = "maui_jobs_list"), Description("List background jobs registered on the device (Android Workers via WorkManager, iOS BGTasks via BGTaskScheduler).")]
	public static async Task<string> ListJobs(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.GetJobsAsync();
		return result.ValueKind == JsonValueKind.Undefined ? "Failed to list jobs." : result.ToString();
	}

	[McpServerTool(Name = "maui_jobs_run"), Description("Trigger a background job by identifier. On Android, re-enqueues a WorkManager worker by tag. On iOS, submits a BGTaskRequest for the given identifier.")]
	public static async Task<string> RunJob(
		McpAgentSession session,
		[Description("Job identifier (Android worker tag or iOS BGTask identifier)")] string identifier,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.RunJobAsync(identifier);
		return result.ValueKind == JsonValueKind.Undefined ? $"Failed to run job '{identifier}'." : result.ToString();
	}
}
