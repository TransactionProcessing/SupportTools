using MerchantPos.EF.Models;
using Microsoft.EntityFrameworkCore;

namespace MerchantPos.EF.Persistence
{
    public interface IEfRepository
    {
        Task<List<Merchant>> GetAllMerchants();
        Task<Decimal> GetBalance(Guid merchantId);
        Task<Merchant> CreateMerchantRecord(Guid merchantId, String merchantName);
        Task UpdateBalance(Guid merchantId, String merchantName, Decimal balance);
        Task UpdateLastEndOfDay(Guid merchantId, String merchantName, DateTime lastEndOfDayDateTime);
        Task UpdateLastLogon(Guid merchantId, String merchantName, DateTime lastLogonDateTime);
        Task UpdateTotals(Guid merchantId, Guid operatorId, Guid contractId, Decimal amount);
        Task<List<OperatorTotal>> GetTotals(Guid merchantId);
        Task ClearTotals(Guid merchantId);
        Task<Merchant> GetMerchant(Guid merchantId);

        Task IncrementTransactionNumber(Guid merchantId, String merchantName);
    }

    public class EfRepository : IEfRepository
    {
        private readonly MerchantDbContext _db;

        public EfRepository(MerchantDbContext db)
        {
            _db = db;
        }

        public async Task<Merchant> CreateMerchantRecord(Guid merchantId,
                                                         String merchantName) {
            Merchant merchant = new Merchant();
            merchant.MerchantId = merchantId;
            merchant.MerchantName = merchantName;
            merchant.Balance = 0;
            merchant.TransactionNumber = 0;
            merchant.LastEndOfDayDateTime = DateTime.MinValue;
            merchant.LastLogonDateTime = DateTime.MinValue;
            _db.Merchants.Add(merchant);
            await _db.SaveChangesAsync();
            return merchant;
        }

        public async Task UpdateBalance(Guid merchantId,String merchantName, Decimal balance)
        {
            Merchant? entry = await this.GetMerchant(merchantId);

            if (entry == null) {
                entry = await this.CreateMerchantRecord(merchantId, merchantName);
            }

            entry.MerchantName = merchantName;
            entry.Balance = balance;
           
            await _db.SaveChangesAsync();
        }

        public async Task<Merchant?> GetMerchant(Guid merchantId)
        {
            return await _db.Merchants.FindAsync(merchantId);
        }

        public async Task IncrementTransactionNumber(Guid merchantId,
                                                     String merchantName) {
            Merchant? entry = await this.GetMerchant(merchantId);

            if (entry == null)
            {
                entry = await this.CreateMerchantRecord(merchantId, merchantName);
            }

            Int32 nextTransactionNumber = entry.TransactionNumber + 1;
            if (nextTransactionNumber == 9999) {
                nextTransactionNumber = 1;
            }
            entry.TransactionNumber = nextTransactionNumber;
            await _db.SaveChangesAsync();
        }

        public async Task UpdateLastEndOfDay(Guid merchantId, String merchantName, DateTime lastEndOfDayDateTime)
        {
            Merchant? entry = await this.GetMerchant(merchantId);

            if (entry == null)
            {
                entry = await this.CreateMerchantRecord(merchantId, merchantName);
            }
            entry.LastEndOfDayDateTime = lastEndOfDayDateTime;
            
            await _db.SaveChangesAsync();
        }

        public async Task UpdateLastLogon(Guid merchantId,
                                          String merchantName,
                                          DateTime lastLogonDateTime) {
            Merchant? entry = await this.GetMerchant(merchantId);

            if (entry == null)
            {
                entry = await this.CreateMerchantRecord(merchantId, merchantName);
            }
            entry.LastLogonDateTime = lastLogonDateTime;

            await _db.SaveChangesAsync();
        }


        public async Task<List<Merchant>> GetAllMerchants() {
            var entries = await _db.Merchants.ToListAsync();
            return entries.ToList();
        }

        public async Task<decimal> GetBalance(Guid merchantId)
        {
            Merchant? entry = await this.GetMerchant(merchantId);
            return entry?.Balance ?? 0;
        }

        public async Task<DateTime> GetLastEndOfDay(Guid merchantId)
        {
            Merchant? entry = await this.GetMerchant(merchantId);
            return entry?.LastEndOfDayDateTime ?? DateTime.MinValue;
        }

        public async Task UpdateTotals(Guid merchantId, Guid operatorId, Guid contractId, decimal amount)
        {
            OperatorTotal? entry = await _db.OperatorTotals
                .SingleOrDefaultAsync(o => o.MerchantId == merchantId && o.OperatorId == operatorId
                && o.ContractId == contractId);

            if (entry == null)
            {
                entry = new OperatorTotal
                {
                    MerchantId = merchantId,
                    OperatorId = operatorId,
                    ContractId = contractId,
                    Total = amount
                };
                _db.OperatorTotals.Add(entry);
            }
            else
            {
                entry.Total += amount;
            }

            await _db.SaveChangesAsync();
        }

        public async Task<List<OperatorTotal>> GetTotals(Guid merchantId)
        {
            return await _db.OperatorTotals
                .Where(o => o.MerchantId == merchantId)
                .ToListAsync();
        }

        public async Task ClearTotals(Guid merchantId)
        {
            IQueryable<OperatorTotal> rows = _db.OperatorTotals.Where(o => o.MerchantId == merchantId);
            _db.OperatorTotals.RemoveRange(rows);
            await _db.SaveChangesAsync();
        }
    }
}

