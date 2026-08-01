using Microsoft.AspNetCore.Mvc.RazorPages;
using TransactionProcessing.MerchantPos.Runtime;
using TransactionProcessing.MerchantPos.Web;

namespace TransactionProcessing.MerchantPos.Pages;

public sealed class IndexModel : PageModel
{
    private readonly MerchantDashboardModelFactory _dashboardFactory;

    public IndexModel(MerchantDashboardModelFactory dashboardFactory)
    {
        _dashboardFactory = dashboardFactory;
    }

    public MerchantDashboardViewModel Dashboard { get; private set; } = default!;

    public async Task OnGetAsync()
    {
        ViewData["ActiveNav"] = "dashboard";
        ViewData["Title"] = "Merchant POS Dashboard";
        Dashboard = await _dashboardFactory.BuildAsync();
    }
}
