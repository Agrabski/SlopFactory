using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace SlopFactory.Services;

public interface IChatClientFactory
{
	AIAgent CreateAgent(SlopServiceOptions options, IList<AITool> tools);
}
