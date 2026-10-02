using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Data;

public class Usuario : IdentityUser
{
    public string NombreCompleto { get; set; } = "";
    public int? PrestadorId { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>Áreas que gestiona un usuario de Operaciones (o CEX, Public, BHT, MSU, AUM): solo ve las planillas de esas áreas.</summary>
public class UsuarioArea
{
    public string UsuarioId { get; set; } = "";
    public Usuario Usuario { get; set; } = null!;
    public int AreaId { get; set; }
    public Area Area { get; set; } = null!;
}

public class AppDbContext(DbContextOptions<AppDbContext> options, IUsuarioActual usuario, TimeProvider reloj)
    : IdentityDbContext<Usuario>(options)
{
    public DbSet<Ciclo> Ciclos => Set<Ciclo>();
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<Glosa> Glosas => Set<Glosa>();
    public DbSet<Banco> Bancos => Set<Banco>();
    public DbSet<TipoCuenta> TiposCuenta => Set<TipoCuenta>();
    public DbSet<TipoGasto> TiposGasto => Set<TipoGasto>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Planilla> Planillas => Set<Planilla>();
    public DbSet<PlanillaArchivo> PlanillaArchivos => Set<PlanillaArchivo>();
    public DbSet<Prestador> Prestadores => Set<Prestador>();
    public DbSet<CuentaBancaria> CuentasBancarias => Set<CuentaBancaria>();
    public DbSet<LineaPago> LineasPago => Set<LineaPago>();
    public DbSet<BoletaHonorarios> Boletas => Set<BoletaHonorarios>();
    public DbSet<Observacion> Observaciones => Set<Observacion>();
    public DbSet<Devolucion> Devoluciones => Set<Devolucion>();
    public DbSet<SolicitudCorreccion> SolicitudesCorreccion => Set<SolicitudCorreccion>();
    public DbSet<Transferencia> Transferencias => Set<Transferencia>();
    public DbSet<Auditoria> Auditorias => Set<Auditoria>();
    public DbSet<Parametro> Parametros => Set<Parametro>();
    public DbSet<TasaRetencion> TasasRetencion => Set<TasaRetencion>();
    public DbSet<CorreoSaliente> Correos => Set<CorreoSaliente>();
    public DbSet<UsuarioArea> UsuarioAreas => Set<UsuarioArea>();

    /// <summary>
    /// Áreas visibles para el usuario actual; null = todas (Finanzas, Administrador, prestadores y procesos sin usuario).
    /// Filtra todas las consultas de planillas (y, por la navegación, sus líneas, boletas y observaciones).
    /// </summary>
    public List<int>? AreasVisibles => Alcance.Areas(usuario.Principal);

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        // Estados legibles en la base.
        foreach (var t in new[] { typeof(CicloEstado), typeof(PlanillaEstado), typeof(LineaEstado), typeof(CuentaEstado), typeof(CuentaOrigen),
                     typeof(BoletaEstado), typeof(BoletaCanal), typeof(Confianza), typeof(ObservacionEstado), typeof(ObservacionTipo),
                     typeof(DevolucionResultado), typeof(SolicitudEstado), typeof(ResultadoCuenta) })
            b.Properties(t).HaveConversion<string>().HaveMaxLength(40);
        b.Properties<decimal>().HavePrecision(18, 0);
        b.Properties<decimal?>().HavePrecision(18, 0);
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        base.OnModelCreating(m);
        foreach (var fk in m.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys())
                     .Where(fk => !fk.DeclaringEntityType.ClrType.Namespace!.StartsWith("Microsoft")))
            fk.DeleteBehavior = DeleteBehavior.Restrict;

        m.Entity<Ciclo>(e => { e.HasIndex(x => x.Periodo).IsUnique(); e.HasIndex(x => x.Codigo).IsUnique(); e.Property(x => x.Codigo).HasMaxLength(10); });
        m.Entity<Area>(e => { e.HasIndex(x => x.Nombre).IsUnique(); e.Property(x => x.Nombre).HasMaxLength(100); e.Property(x => x.CodigoArea).HasMaxLength(20);
            e.Property(x => x.Perfil).HasMaxLength(40).HasDefaultValue(IpsosPagoHonorarios.Core.Roles.Operaciones); });
        m.Entity<Glosa>(e => { e.HasIndex(x => x.NombreGlosa).IsUnique(); e.Property(x => x.NombreGlosa).HasMaxLength(150); e.Property(x => x.Item).HasMaxLength(20); e.Property(x => x.CuentaContable).HasMaxLength(20); });
        m.Entity<Banco>(e => { e.HasIndex(x => x.Nombre).IsUnique(); e.Property(x => x.Nombre).HasMaxLength(60); e.Property(x => x.CodigoBanco).HasMaxLength(10); });
        m.Entity<TipoCuenta>(e => { e.HasIndex(x => x.Nombre).IsUnique(); e.Property(x => x.Nombre).HasMaxLength(40); e.Property(x => x.Codigo).HasMaxLength(10); });
        m.Entity<TipoGasto>(e => { e.HasIndex(x => x.Nombre).IsUnique(); e.Property(x => x.Nombre).HasMaxLength(40); });
        m.Entity<Job>(e =>
        {
            e.HasIndex(x => x.JobBookNumber).IsUnique();
            e.Property(x => x.JobBookNumber).HasMaxLength(12).IsFixedLength();
            e.Property(x => x.Nombre).HasMaxLength(200);
            e.Property(x => x.ValorUnitarioSugerido).HasPrecision(18, 2);
        });
        m.Entity<Planilla>(e =>
        {
            e.HasIndex(x => new { x.CicloId, x.AreaId }).IsUnique();
            e.HasQueryFilter(x => AreasVisibles == null || AreasVisibles.Contains(x.AreaId));
            e.HasMany(x => x.Lineas).WithOne(x => x.Planilla).HasForeignKey(x => x.PlanillaId);
            e.HasMany(x => x.Boletas).WithOne(x => x.Planilla).HasForeignKey(x => x.PlanillaId);
            e.HasMany(x => x.Devoluciones).WithOne(x => x.Planilla).HasForeignKey(x => x.PlanillaId);
            e.Property(x => x.ResponsableNombre).HasMaxLength(150);
            e.Property(x => x.ResponsableEmail).HasMaxLength(200);
        });
        m.Entity<Prestador>(e =>
        {
            e.HasIndex(x => x.Rut).IsUnique();
            e.Property(x => x.Dv).HasMaxLength(1);
            e.Property(x => x.NombreCompleto).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Telefono).HasMaxLength(30);
            e.HasMany(x => x.Cuentas).WithOne(x => x.Prestador).HasForeignKey(x => x.PrestadorId);
            e.Ignore(x => x.RutPlanilla);
            e.Ignore(x => x.RutUi);
        });
        m.Entity<CuentaBancaria>(e =>
        {
            e.HasIndex(x => new { x.Banco, x.CuentaNormalizada });
            e.Property(x => x.Banco).HasMaxLength(60);
            e.Property(x => x.TipoCuenta).HasMaxLength(40);
            e.Property(x => x.Cuenta).HasMaxLength(20);
            e.Property(x => x.CuentaNormalizada).HasMaxLength(20);
            e.Ignore(x => x.Descripcion);
        });
        m.Entity<LineaPago>(e =>
        {
            e.Property(x => x.ValorUnitarioBruto).HasPrecision(18, 2);
            e.Property(x => x.Cantidad).HasPrecision(18, 4);
            e.Property(x => x.TipoGasto).HasMaxLength(40);
            e.Property(x => x.NumeroBoleta).HasMaxLength(20);
            e.Property(x => x.CuentaPlanillaTipo).HasMaxLength(40);
            e.Property(x => x.CuentaPlanillaNumero).HasMaxLength(30);
            e.Property(x => x.CuentaPlanillaBanco).HasMaxLength(60);
            e.Ignore(x => x.AlertaAbierta);
        });
        m.Entity<BoletaHonorarios>(e =>
        {
            e.HasIndex(x => x.HashPdf);
            e.HasIndex(x => new { x.RutEmisor, x.NumeroBoleta });
            e.Property(x => x.HashPdf).HasMaxLength(64);
            e.Property(x => x.NumeroBoleta).HasMaxLength(20);
            e.Property(x => x.RutEmisorDv).HasMaxLength(1);
            e.Property(x => x.RutReceptor).HasMaxLength(12);
            e.Ignore(x => x.Vigente);
        });
        m.Entity<Observacion>(e => { e.Property(x => x.Campo).HasMaxLength(100); e.Property(x => x.Detalle).HasMaxLength(1000); });
        m.Entity<Transferencia>(e => { e.HasIndex(x => x.BoletaId).IsUnique(); e.Property(x => x.NumeroOperacion).HasMaxLength(50); });
        m.Entity<TasaRetencion>(e => { e.HasIndex(x => x.Anio).IsUnique(); e.Property(x => x.Tasa).HasPrecision(9, 4); });
        m.Entity<UsuarioArea>(e =>
        {
            e.HasKey(x => new { x.UsuarioId, x.AreaId });
            e.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId);
            e.HasOne(x => x.Area).WithMany().HasForeignKey(x => x.AreaId);
        });
        m.Entity<Auditoria>(e => { e.HasIndex(x => x.Fecha); e.Property(x => x.Entidad).HasMaxLength(60); e.Property(x => x.Accion).HasMaxLength(100); });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        Sellar();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        Sellar();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    private void Sellar()
    {
        var ahora = reloj.GetUtcNow().UtcDateTime;
        foreach (var e in ChangeTracker.Entries<EntidadAuditable>())
        {
            if (e.State == EntityState.Added)
            {
                e.Entity.CreadoEn = ahora;
                e.Entity.CreadoPor ??= usuario.Nombre;
            }
            else if (e.State == EntityState.Modified)
            {
                e.Entity.ModificadoEn = ahora;
                e.Entity.ModificadoPor = usuario.Nombre;
            }
        }
    }
}
