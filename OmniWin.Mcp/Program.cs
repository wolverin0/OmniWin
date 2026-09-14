using System.Threading.Tasks;
using OmniWin.Core.Mcp;

namespace OmniWin.Mcp;

public class Program
{
    public static async Task Main(string[] args)
    {
        var server = new McpServer();
        await server.RunAsync();
    }
}
