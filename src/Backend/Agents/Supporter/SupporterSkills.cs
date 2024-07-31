
namespace eShopSupport.Backend.Agents;
public static class SupporterSkills {
    public static string Respond = """
        You are a very helpful support center professional. 
        Your task is to respond to the user's request with the most appropriate response.
        Input: {{$input}}
        """;
    public static string Revise = """
        You are a very helpful support center professional. 
        Your task is to revise your previous response
        {{$response}}
        based on the provided feedback by the auditor
        {{$feedback}} 
        to the original user's ask with the most appropriate response.
        Original ask: {{$input}}
        """;
}