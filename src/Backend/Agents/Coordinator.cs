namespace eShopSupport.Backend.Agents;

public class Conductor : Grain, ICoordinateSupportAgents
{
    public async Task<string> ForwardToTeam(string ask)
    {
            var iterations = 0; 
            var supporter = GrainFactory.GetGrain<ISupportTickets>(this.GetPrimaryKeyString());
            var auditor = GrainFactory.GetGrain<IAuditReponses>(this.GetPrimaryKeyString());

            var response = await supporter.RespondToUser(ask);
            var audit = await auditor.AuditResponse(response);

            while (iterations < 5 && !audit.Success)
            {
                var currentResponse = response;
                response = await supporter.ReviseResponse(ask, currentResponse, audit.Feedback);
                audit = await auditor.AuditResponse(response);
                iterations++;
            }

            return audit.Success ? response : "Sorry, we are unable to provide a response at this time.";
    }
}

public interface ICoordinateSupportAgents : IGrainWithStringKey
{
    Task<string> ForwardToTeam(string ask);
}