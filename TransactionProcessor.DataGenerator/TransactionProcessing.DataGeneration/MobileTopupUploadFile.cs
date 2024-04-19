namespace TransactionProcessing.DataGeneration;

public class MobileTopupUploadFile : UploadFile{
    #region Constructors

    public MobileTopupUploadFile(Guid estateId, Guid merchantId, Guid contractId, Guid productId, Guid fileProfileId, Guid userId) : base(estateId, merchantId, contractId, productId, fileProfileId,userId){
    }

    #endregion

    #region Methods

    public void AddLine(Decimal amount, String mobileNumber){
        this.TotalValue += amount;
        this.FileLines.Add($"D,{mobileNumber},{amount}");
        this.FileLineCount++;
        this.TotalAmount += amount;
    }

    #endregion
}