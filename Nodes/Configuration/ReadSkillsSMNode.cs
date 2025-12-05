using Microsoft.Extensions.Logging;
using MAS_BT.Core;

namespace MAS_BT.Nodes.Configuration;

/// <summary>
/// ReadSkillsSM - Loads the list of available skills from the AAS
/// </summary>
public class ReadSkillsSMNode : BTNode
{
    public string AgentId { get; set; } = string.Empty;
    
    public ReadSkillsSMNode() : base("ReadSkillsSM")
    {
    }
    
    public override async Task<NodeStatus> Execute()
    {
        Logger.LogDebug("ReadSkillsSM: Reading skills for {AgentId}", AgentId);
        
        try
        {
            // TODO: Integration mit AAS-Sharp-Client
            // var aasClient = Context.Get<AASClient>("AASClient");
            // var shell = await aasClient.GetShellById(AgentId);
            // var skills = shell.GetSubmodelById("Skills");
            
            // Mockup
            var skills = new
            {
                IdShort = "Skills",
                AvailableSkills = new[]
                {
                    new { Name = "StartupSkill", Description = "Initialize module", Duration = 5000 },
                    new { Name = "AssemblySkill", Description = "Assemble parts", Duration = 30000 },
                    new { Name = "ShutdownSkill", Description = "Shutdown module", Duration = 3000 }
                }
            };
            
            Context.Set("skills", skills);
            
            Logger.LogInformation("ReadSkillsSM: Successfully read {Count} skills for {AgentId}", 
                skills.AvailableSkills.Length, AgentId);
            
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ReadSkillsSM: Error reading skills for {AgentId}", AgentId);
            return NodeStatus.Failure;
        }
    }
}
