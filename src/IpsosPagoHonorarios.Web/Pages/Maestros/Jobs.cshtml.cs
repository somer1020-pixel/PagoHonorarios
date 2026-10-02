using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using IpsosPagoHonorarios.Web.Infraestructura;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Pages.Maestros;

/// <summary>Jobs, glosas y catálogos: Finanzas mantiene, Operaciones lee.</summary>
public class JobsModel(AppDbContext db, Auditor auditor) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public int? Editar { get; set; }
    public List<Job> Jobs { get; set; } = [];
    public List<Glosa> Glosas { get; set; } = [];
    public List<Area> Areas { get; set; } = [];
    public List<Banco> Bancos { get; set; } = [];
    public List<TipoCuenta> Tipos { get; set; } = [];
    public List<TipoGasto> Gastos { get; set; } = [];
    public Job? Sel { get; set; }
    public bool PuedeMantener => Puede(Roles.Finanzas);

    public async Task OnGetAsync()
    {
        Jobs = await db.Jobs.Include(j => j.AreaSugerida).OrderBy(j => j.JobBookNumber).ToListAsync();
        Glosas = await db.Glosas.OrderBy(g => g.Item).ToListAsync();
        Areas = await db.Areas.OrderBy(a => a.Nombre).ToListAsync();
        Bancos = await db.Bancos.OrderBy(b => b.Id).ToListAsync();
        Tipos = await db.TiposCuenta.OrderBy(t => t.Id).ToListAsync();
        Gastos = await db.TiposGasto.OrderBy(t => t.Id).ToListAsync();
        Sel = Editar is null ? null : Jobs.FirstOrDefault(j => j.Id == Editar);
    }

    public Task<IActionResult> OnPostJobAsync(int? id, string jobBookNumber, string nombre, int? areaId, string? valorUnitario, bool activo) =>
        AccionAsync(async () =>
        {
            jobBookNumber = jobBookNumber?.Trim() ?? "";
            if (!ProduccionReglas.JobValido(jobBookNumber)) throw new ReglaException("El Job Book Number debe tener 12 dígitos.");
            if (string.IsNullOrWhiteSpace(nombre)) throw new ReglaException("El nombre es obligatorio.");
            if (await db.Jobs.AnyAsync(j => j.JobBookNumber == jobBookNumber && j.Id != id)) throw new ReglaException("Ya existe ese Job.");
            var vu = ExcelPlanilla.ParseNumero(valorUnitario);
            if (vu is <= 0) throw new ReglaException("El valor unitario debe ser mayor que 0.");
            var j = id is null ? new Job() : await db.Jobs.FirstAsync(x => x.Id == id);
            if (id is null) db.Jobs.Add(j);
            j.JobBookNumber = jobBookNumber;
            j.Nombre = nombre.Trim();
            j.AreaSugeridaId = areaId;
            j.ValorUnitarioSugerido = vu;
            j.Activo = activo;
            auditor.Registrar(nameof(Job), jobBookNumber, id is null ? "Crear Job" : "Editar Job", j.Nombre);
            await db.SaveChangesAsync();
        }, "Job guardado.", null, [Roles.Finanzas]);

    /// <summary>Nueva área (p. ej. para CEX): luego el Administrador la asigna a los usuarios en Maestros → Usuarios.</summary>
    public Task<IActionResult> OnPostAreaAsync(string nombreArea, string codigoArea) =>
        AccionAsync(async () =>
        {
            nombreArea = nombreArea?.Trim() ?? "";
            codigoArea = codigoArea?.Trim() ?? "";
            if (nombreArea.Length == 0 || codigoArea.Length == 0) throw new ReglaException("Nombre y código del área son obligatorios (exactos al catálogo de Finanzas).");
            if (await db.Areas.AnyAsync(a => a.Nombre == nombreArea)) throw new ReglaException("El área ya existe.");
            db.Areas.Add(new Area { Nombre = nombreArea, CodigoArea = codigoArea });
            auditor.Registrar(nameof(Area), nombreArea, "Crear área", codigoArea);
            await db.SaveChangesAsync();
        }, "Área agregada. Asígnala a los usuarios que la gestionan en Maestros → Usuarios.", null, [Roles.Finanzas]);

    public Task<IActionResult> OnPostGlosaAsync(string nombreGlosa, string item, string cuentaContable) =>
        AccionAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(nombreGlosa) || string.IsNullOrWhiteSpace(item) || string.IsNullOrWhiteSpace(cuentaContable))
                throw new ReglaException("Nombre glosa, item y cuenta contable son obligatorios (exactos al catálogo de Finanzas).");
            if (await db.Glosas.AnyAsync(g => g.NombreGlosa == nombreGlosa.Trim())) throw new ReglaException("La glosa ya existe.");
            db.Glosas.Add(new Glosa { NombreGlosa = nombreGlosa.Trim(), Item = item.Trim(), CuentaContable = cuentaContable.Trim() });
            auditor.Registrar(nameof(Glosa), nombreGlosa, "Crear glosa", $"{item}/{cuentaContable}");
            await db.SaveChangesAsync();
        }, "Glosa agregada.", null, [Roles.Finanzas]);
}
