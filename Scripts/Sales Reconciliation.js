
select CONCAT(YEAR(transactiondate),'-', FORMAT(transactiondate, 'MM')),
count(*) as 'count',
SUM(case [transaction].TransactionType
		WHEN 'Logon' THEN 1
		ELSE 0 END) as logoncount,
SUM(case [transaction].TransactionType
		WHEN 'Sale' THEN 1
		ELSE 0 END) as salecount,
SUM(case [transaction].IsCompleted
		WHEN 1 THEN 1
		ELSE 0 END) as completedcount,
SUM(case [transaction].IsAuthorised
		WHEN 1 THEN 1
		ELSE 0 END) as authorisedcount,
SUM(case [transaction].IsAuthorised
		WHEN 0 THEN 1
		ELSE 0 END) as failedcount,
SUM([transaction].TransactionAmount) as totalamount
from [transaction] 
group by CONCAT(YEAR(transactiondate),'-', FORMAT(transactiondate, 'MM'))
order by CONCAT(YEAR(transactiondate),'-', FORMAT(transactiondate, 'MM')) asc


fromAll()
  .when({
    $init: function () {
      return {
        monthlySales: {}
      };
    },

    TransactionHasStartedEvent: function (state, event) {
      const data = event.body;

      // Check transactionType
      if (data.transactionType !== "Sale" && data.transactionType !== "Logon") {
        return state;
      }

      const monthKey = getMonthKey(data.transactionDateTime);
      const monthly = ensureMonthEntry(state, monthKey);

      monthly.count += 1;
      if (data.transactionType === "Sale") {
        monthly.saleCount += 1;
        if (typeof data.transactionAmount !== "number") {
          monthly.totalAmount += 0;
          return state;
        }
        monthly.totalAmount += data.transactionAmount;
      }

      if (data.transactionType === "Logon") {
        monthly.logonCount += 1;
      }

      return state;
    },

    TransactionHasBeenCompletedEvent: function (state, event) {
      const data = event.body;
      const monthKey = getMonthKey(data.transactionDateTime);
      const monthly = ensureMonthEntry(state, monthKey);

      monthly.completedCount += 1;

      if (data.isAuthorised === true) {
        monthly.authorisedcount += 1;
      } else {
        monthly.failedCount += 1;
      }

      return state;
    }
  });

function getMonthKey(dateString) {
  const date = new Date(dateString);
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
}

function ensureMonthEntry(state, monthKey) {
  if (!state.monthlySales.hasOwnProperty(monthKey)) {
    state.monthlySales[monthKey] = {
      count: 0,
      logonCount: 0,
      saleCount: 0,
      completedCount: 0,
      authorisedcount: 0,
      failedCount: 0,
      totalAmount: 0,
      
      
    };
  }

  return state.monthlySales[monthKey];
}
