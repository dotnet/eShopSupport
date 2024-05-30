namespace eShopSupport.Evaluator;

public class EvalQuestion
{
    public int QuestionId { get; set; }

    public int? ProductId { get; set; }

    public required string Question { get; set; }

    public required string Answer { get; set; }
}
