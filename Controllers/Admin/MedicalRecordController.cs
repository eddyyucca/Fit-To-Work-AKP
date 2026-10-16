using FitToWorkApp.Models;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers.Admin
{
    /// <summary>
    /// Rekam medis per karyawan (gabungan pemeriksaan kesehatan + kunjungan klinik) dan
    /// pencarian karyawan untuk form input medis. Butuh salah satu hak akses medis.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.MedicalCheckup, Permissions.MedicalVisit, Permissions.MedicalDrugTest)]
    [Route("Admin/MedicalRecord")]
    public class MedicalRecordController : Controller
    {
        private readonly IEmployeeRepository _employeeRepository;
        private readonly IHealthCheckupRepository _checkupRepository;
        private readonly IClinicVisitRepository _visitRepository;
        private readonly IDrugAlcoholTestRepository _drugTestRepository;
        private readonly IPermissionService _permissionService;
        private readonly IAppClock _clock;

        public MedicalRecordController(IEmployeeRepository employeeRepository, IHealthCheckupRepository checkupRepository,
            IClinicVisitRepository visitRepository, IDrugAlcoholTestRepository drugTestRepository,
            IPermissionService permissionService, IAppClock clock)
        {
            _drugTestRepository = drugTestRepository;
            _employeeRepository = employeeRepository;
            _checkupRepository = checkupRepository;
            _visitRepository = visitRepository;
            _permissionService = permissionService;
            _clock = clock;
        }

        [HttpGet("")]
        public IActionResult Index() => View();

        // GET Admin/MedicalRecord/SearchEmployee?q=..&includeMitra=true  (autocomplete: NIP / nama)
        // includeMitra = true hanya di form kunjungan klinik & halaman rekam medis;
        // pemeriksaan kesehatan & tes urin tetap karyawan AKP saja.
        [HttpGet("SearchEmployee")]
        public async Task<IActionResult> SearchEmployee(string? q, bool includeMitra = false)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
                return Json(Array.Empty<object>());

            var today = _clock.Today;
            var employees = await _employeeRepository.GetAllAsync(q.Trim(), includeMitra ? EmployeeScope.All : EmployeeScope.Akp);
            return Json(employees.Where(e => e.IsActive).Take(15).Select(e => new
            {
                id = e.Id,
                nip = e.NIP,
                nama = e.Nama,
                department = e.UnitKerja,
                section = e.BagianKerja,
                position = e.JabatanKerja,
                mitra = e.IsMitra,
                jenisKelamin = e.JenisKelamin,
                umur = e.TanggalLahir.HasValue ? Age(e.TanggalLahir.Value, today) : (int?)null
            }));
        }

        [HttpGet("Employee/{id:int}")]
        public async Task<IActionResult> Employee(int id)
        {
            var employee = await _employeeRepository.GetByIdAsync(id);
            if (employee is null)
                return NotFound();

            var access = await _permissionService.GetAsync(User);
            // Karyawan mitra: kunjungan klinik + tes urin/alkohol (tanpa pemeriksaan kesehatan/pemkes)
            var akp = !employee.IsMitra;
            ViewBag.CanCheckup = akp && access.Has(Permissions.MedicalCheckup);
            ViewBag.CanVisit = access.Has(Permissions.MedicalVisit);
            ViewBag.Checkups = akp && access.Has(Permissions.MedicalCheckup) ? await _checkupRepository.GetByEmployeeAsync(id) : new();
            ViewBag.Visits = access.Has(Permissions.MedicalVisit) ? await _visitRepository.GetByEmployeeAsync(id, includeCancelled: true) : new();
            ViewBag.CanDrugTest = access.Has(Permissions.MedicalDrugTest);
            ViewBag.DrugTests = access.Has(Permissions.MedicalDrugTest) ? await _drugTestRepository.GetByEmployeeAsync(id) : new();
            ViewBag.Today = _clock.Today;
            ViewBag.Umur = employee.TanggalLahir.HasValue ? Age(employee.TanggalLahir.Value, _clock.Today) : (int?)null;
            return View(employee);
        }

        public static int Age(DateTime birth, DateTime today)
        {
            var age = today.Year - birth.Year;
            if (birth.Date > today.AddYears(-age)) age--;
            return age;
        }
    }
}
