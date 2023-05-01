namespace TransactionProcessing.DataGeneration;

using Newtonsoft.Json;
using TransactionProcessor.DataTransferObjects;

public static class Extensions{
    #region Methods

    public static Decimal GetAmount(this SaleTransactionRequest request){
        if (request.AdditionalTransactionMetadata.ContainsKey("Amount")){
            return Decimal.Parse(request.AdditionalTransactionMetadata["Amount"]);
        }

        return 0;
    }

    public static SerialisedMessage CreateSerialisedMessage(this LogonTransactionRequest request){
        SerialisedMessage serialisedMessage = new SerialisedMessage();
        serialisedMessage.Metadata.Add("estate_id", request.EstateId.ToString());
        serialisedMessage.Metadata.Add("merchant_id", request.MerchantId.ToString());
        serialisedMessage.SerialisedData = JsonConvert.SerializeObject(request,
                                                                       new JsonSerializerSettings
                                                                       {
                                                                           TypeNameHandling = TypeNameHandling.All
                                                                       });

        return serialisedMessage;
    }

    public static SerialisedMessage CreateSerialisedMessage(this SaleTransactionRequest request)
    {
        SerialisedMessage serialisedMessage = new SerialisedMessage();
        serialisedMessage.Metadata.Add("estate_id", request.EstateId.ToString());
        serialisedMessage.Metadata.Add("merchant_id", request.MerchantId.ToString());
        serialisedMessage.SerialisedData = JsonConvert.SerializeObject(request,
                                                                       new JsonSerializerSettings
                                                                       {
                                                                           TypeNameHandling = TypeNameHandling.All
                                                                       });

        return serialisedMessage;
    }

    public static T GetSerialisedMessageResponseDTO<T>(this SerialisedMessage serialisedMessage){
        return JsonConvert.DeserializeObject<T>(serialisedMessage.SerialisedData);
    }

    #endregion
}