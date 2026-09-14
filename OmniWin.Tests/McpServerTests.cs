using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using OmniWin.Core.Mcp;

namespace OmniWin.Tests;

public class McpServerTests
{
    [Fact]
    public void GetToolsList_ReturnsAll28Tools()
    {
        var tools = McpServer.GetToolsList();

        Assert.NotNull(tools);
        Assert.True(tools.Count >= 28, $"Expected at least 28 tools, found {tools.Count}");
    }

    [Theory]
    [InlineData("win_get_system_health")]
    [InlineData("win_purge_ram")]
    [InlineData("win_analyze_disk_bloat")]
    [InlineData("win_clean_disk")]
    [InlineData("win_test_network")]
    [InlineData("win_heal_network")]
    [InlineData("win_get_security_audit")]
    [InlineData("win_get_software_updates")]
    [InlineData("win_upgrade_all_software")]
    [InlineData("win_clean_dism_store")]
    [InlineData("win_run_sfc_scan")]
    [InlineData("win_list_drivers")]
    [InlineData("win_get_power_schemes")]
    [InlineData("win_set_power_scheme")]
    [InlineData("win_list_processes")]
    [InlineData("win_kill_process")]
    [InlineData("win_list_startup_items")]
    [InlineData("win_toggle_startup_item")]
    [InlineData("win_list_tweaks")]
    [InlineData("win_apply_tweak")]
    [InlineData("win_rollback_tweak")]
    [InlineData("win_find_file_locks")]
    [InlineData("win_unlock_file")]
    [InlineData("win_list_windows_services")]
    [InlineData("win_set_service_state")]
    [InlineData("win_optimize_services")]
    [InlineData("win_list_context_menus")]
    [InlineData("win_toggle_context_menu")]
    public void GetToolsList_ContainsExpectedTool(string expectedToolName)
    {
        var tools = McpServer.GetToolsList();
        var tool = tools.OfType<JsonObject>().FirstOrDefault(t => t["name"]?.GetValue<string>() == expectedToolName);

        Assert.NotNull(tool);
        Assert.False(string.IsNullOrWhiteSpace(tool["description"]?.GetValue<string>()), $"Tool {expectedToolName} has empty description");
        Assert.NotNull(tool["inputSchema"]);
    }
}
