using System.Security.Claims;
using FitToWorkApp.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers
{
    /// <summary>
    /// Menu utama karyawan setelah login NIP: pilih "Fit To Work", "Aktivitas Kebugaran" atau "Riwayat Saya".
    /// </summary>
    [Authorize(Policy = "EmployeeOnly")]
    public class MenuController : Controller
    {
        private readonly IEmployeeRepository _employeeRepository;
        private readonly IGymRepository _gymRepository;

        public MenuController(IEmployeeRepository employeeRepository, IGymRepository gymRepository)
        {
            _employeeRepository = employeeRepository;
            _gymRepository = gymRepository;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var employeeId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var employee = await _employeeRepository.GetByIdAsync(employeeId);
            if (employee is null || !employee.IsActive)
                return RedirectToAction("EmployeeLogin", "Account");

            // Kalau masih ada aktivitas berlangsung, tile Aktivitas menampilkan pengingat untuk menyelesaikan
            ViewBag.GymOpen = await _gymRepository.GetOpenAsync(employee.Id);
            return View(employee);
        }
    }
}
