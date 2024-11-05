using Microsoft.Extensions.VectorData;

namespace eShopSupport.Backend.Data;

public class ManualChunk
{
    [VectorStoreRecordData]
    public int ChunkId { get; set; }
    [VectorStoreRecordKey]
    public int ProductId { get; set; }
    [VectorStoreRecordData]
    public int PageNumber { get; set; }
    [VectorStoreRecordData]
    public required string Text { get; set; }

    [VectorStoreRecordVector(384,DistanceFunction.CosineDistance)]
    public required byte[] Embedding { get; set; }
}
