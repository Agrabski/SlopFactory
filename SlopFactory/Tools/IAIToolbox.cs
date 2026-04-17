using Microsoft.Extensions.AI;

namespace SlopFactory.Tools;

public interface IAIToolbox
{
	IList<AITool> GetTools();
}
