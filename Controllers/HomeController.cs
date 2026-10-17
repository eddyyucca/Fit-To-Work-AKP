using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers
{
    [AllowAnonymous]
    public class HomeController : Controller
    {
        // Tanpa [HttpGet]: UseExceptionHandler menjalankan ulang halaman ini dengan metode request asli. Kalau hanya GET,
        // exception pada request POST (login, simpan) tampil sebagai "405 Method Not Allowed" dan penyebab aslinya tersembunyi.
        [AcceptVerbs("GET", "POST", "PUT", "PATCH", "DELETE")]
        public IActionResult Error() => View();
    }
}
