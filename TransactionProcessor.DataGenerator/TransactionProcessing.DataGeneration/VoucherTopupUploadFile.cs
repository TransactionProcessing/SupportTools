namespace TransactionProcessing.DataGeneration;

public class VoucherTopupUploadFile : UploadFile{
    #region Constructors

    public VoucherTopupUploadFile(Guid estateId, Guid merchantId, Guid contractId, Guid productId, Guid fileProfileId, Guid userId) : base(estateId, merchantId, contractId, productId,  fileProfileId, userId){
    }

    #endregion

    #region Methods

    public void AddLine(Decimal amount, String recipient, String issuerName){
        this.TotalValue += amount;
        this.FileLines.Add($"D,{issuerName},{recipient},{amount}");
        this.FileLineCount++;
        this.TotalAmount += amount;
    }

    #endregion
}