using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace SlopFactory.Services;

public interface IChatClientFactory
{
	Workflow CreateAgent(SlopServiceOptions options, IList<AITool> tools);
}
