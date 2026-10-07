using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Services;

public sealed record ResultadoCargaInicial(int Unicas, int ConVarias, int Compartidas, int Omitidas, List<string> Detalle);

public sealed record FilaSolicitud(int Fila, string Prestador, string Resultado, string QueCorregir);

/// <summary>Cuentas bancarias: registro, validación, carga inicial y validación al subir la planilla.</summary>
public class CuentasService(AppDbContext db, CicloService ciclos, Auditor auditor, Correos correos, IUsuarioActual usuario)
{
    private async Task<(List<string> Bancos, List<string> Tipos)> CatalogosAsync() =>
        (await db.Bancos.Select(b => b.Nombre).ToListAsync(), await db.TiposCuenta.Select(t => t.Nombre).ToListAsync());

    /// <summary>Dueño (RUT y descripción) de una cuenta (banco + normalizada) registrada.</summary>
    private async Task<Func<string, string, (int Rut, string Descripcion)?>> DuenosAsync()
    {
        var cuentas = await db.CuentasBancarias.Include(c => c.Prestador)
            .Where(c => c.Estado != CuentaEstado.Rechazada).AsNoTracking().ToListAsync();
        var mapa = cuentas.GroupBy(c => (c.Banco.ToUpperInvariant(), c.CuentaNormalizada))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.Vigente).First());
        return (banco, norm) => mapa.TryGetValue((banco.ToUpperInvariant(), norm), out var c)
            ? (c.Prestador.Rut, $"{c.Prestador.RutUi} {c.Prestador.NombreCompleto}")
            : null;
    }

    /// <summary>
    /// R-07 / R-23: registro o cambio de cuenta por Operaciones o Finanzas, sin respaldo documental. Queda pendiente de
    /// validación; la anterior pasa a Inactiva. Luego se revalidan las planillas abiertas del prestador.
    /// </summary>
    public async Task<CuentaBancaria> RegistrarAsync(int prestadorId, string banco, string tipo, string cuenta, string cuentaRepetida, string? detalle = null)
    {
        var prest = await db.Prestadores.Include(p => p.Cuentas).FirstOrDefaultAsync(p => p.Id == prestadorId)
                    ?? throw new ReglaException("Prestador no encontrado.");
        var (bancos, tipos) = await CatalogosAsync();
        var duenos = await DuenosAsync();
        var errores = CuentaReglas.ValidarRegistro(prest.Rut, banco, tipo, cuenta, cuentaRepetida, bancos, tipos,
            (b, n) => duenos(b, n)?.Rut);
        if (errores.Count > 0) throw new ReglaException(string.Join(" ", errores));

        var ahora = ciclos.AhoraUtc;
        foreach (var anterior in prest.Cuentas.Where(c => c.Vigente))
        {
            anterior.Vigente = false;
            if (anterior.Estado != CuentaEstado.Rechazada) anterior.Estado = CuentaEstado.Inactiva;
            anterior.ReemplazadaEn = ahora;
        }
        var nueva = new CuentaBancaria
        {
            PrestadorId = prest.Id,
            Banco = bancos.First(b => b.Equals(banco, StringComparison.OrdinalIgnoreCase)),
            TipoCuenta = tipos.First(t => t.Equals(tipo, StringComparison.OrdinalIgnoreCase)),
            Cuenta = cuenta.Trim(),
            CuentaNormalizada = CuentaReglas.Normalizar(cuenta),
            Estado = CuentaEstado.PendienteValidacion,
            Vigente = true,
            Origen = CuentaOrigen.Registro,
            OrigenDetalle = detalle,
            RegistradaPor = usuario.Nombre,
            RegistradaEn = ahora
        };
        prest.Cuentas.Add(nueva);
        auditor.Registrar(nameof(CuentaBancaria), prest.RutPlanilla, "Registrar cuenta", $"{nueva.Descripcion} · pendiente de validación");
        await db.SaveChangesAsync();
        await RevalidarPrestadorAsync(prest.Id);
        await db.SaveChangesAsync();
        return nueva;
    }

    /// <summary>R-23: solo Finanzas valida.</summary>
    public async Task ValidarAsync(int cuentaId)
    {
        var c = await db.CuentasBancarias.Include(x => x.Prestador).FirstOrDefaultAsync(x => x.Id == cuentaId) ?? throw new ReglaException("Cuenta no encontrada.");
        if (c.Estado != CuentaEstado.PendienteValidacion) throw new ReglaException("La cuenta no está pendiente de validación.");
        c.Estado = CuentaEstado.Validada;
        c.ValidadaPor = usuario.Nombre;
        c.ValidadaEn = ciclos.AhoraUtc;
        c.RequiereRevision = false;
        auditor.Registrar(nameof(CuentaBancaria), c.Prestador.RutPlanilla, "Validar cuenta", c.Descripcion);
        await db.SaveChangesAsync();
    }

    public async Task RechazarAsync(int cuentaId)
    {
        var c = await db.CuentasBancarias.Include(x => x.Prestador).FirstOrDefaultAsync(x => x.Id == cuentaId) ?? throw new ReglaException("Cuenta no encontrada.");
        if (c.Estado != CuentaEstado.PendienteValidacion) throw new ReglaException("La cuenta no está pendiente de validación.");
        c.Estado = CuentaEstado.Rechazada;
        c.Vigente = false;
        c.RequiereRevision = false;
        auditor.Registrar(nameof(CuentaBancaria), c.Prestador.RutPlanilla, "Rechazar cuenta", c.Descripcion);
        await db.SaveChangesAsync();
        await RevalidarPrestadorAsync(c.PrestadorId);
        await db.SaveChangesAsync();
    }

    /// <summary>R-24 / R-25: valida todas las filas activas de la planilla y deja su estado en Borrador o ConAlertasCuenta.</summary>
    public async Task ValidarPlanillaAsync(Planilla p)
    {
        var duenos = await DuenosAsync();
        var activas = p.Activas().ToList();
        foreach (var l in activas)
        {
            var cl = Clasificar(l, activas, duenos);
            l.ResultadoCuenta = cl.Resultado;
            l.ResultadoCuentaDetalle = cl.Detalle;
            l.AlertaCuentaResuelta = false;
            l.CuentaBancariaId = l.Prestador.Cuentas.FirstOrDefault(c => c.Vigente)?.Id;
        }
        ActualizarEstadoAlertas(p);
        auditor.Registrar(nameof(Planilla), p.Id, "Validar cuentas",
            $"{p.Titulo} v{p.Version}: " + string.Join(" · ", activas.GroupBy(l => l.PrestadorId)
                .Select(g => g.First().ResultadoCuenta).GroupBy(r => r).Select(g => $"{g.Key.Nombre()} {g.Count()}")));
    }

    private static Clasificacion Clasificar(LineaPago l, List<LineaPago> activas, Func<string, string, (int, string)?> duenos)
    {
        var vigente = l.Prestador.Cuentas.FirstOrDefault(c => c.Vigente);
        var registrada = vigente is null ? null : new CuentaRef(vigente.Banco, vigente.TipoCuenta, vigente.Cuenta);
        return CuentaReglas.Clasificar(l.Prestador.Rut, RefDe(l), registrada, duenos,
            activas.Where(o => o.PrestadorId == l.PrestadorId && !ReferenceEquals(o, l)).Select(RefDe));
    }

    private static CuentaRef? RefDe(LineaPago l) =>
        string.IsNullOrWhiteSpace(l.CuentaPlanillaNumero) ? null
            : new CuentaRef(l.CuentaPlanillaBanco ?? "", l.CuentaPlanillaTipo ?? "", l.CuentaPlanillaNumero, l.Numero);

    private static void ActualizarEstadoAlertas(Planilla p)
    {
        if (p.Estado is not (PlanillaEstado.Borrador or PlanillaEstado.ConAlertasCuenta)) return;
        p.Estado = p.Activas().Any(l => l.AlertaAbierta) ? PlanillaEstado.ConAlertasCuenta : PlanillaEstado.Borrador;
    }

    /// <summary>Tras registrar o rechazar una cuenta: las alertas que ahora coinciden quedan resueltas (R-25).</summary>
    private async Task RevalidarPrestadorAsync(int prestadorId)
    {
        // La cuenta es del prestador: se revalida en todas las áreas, aunque el usuario solo gestione algunas.
        var ids = await db.LineasPago.IgnoreQueryFilters()
            .Where(l => l.PrestadorId == prestadorId && l.Estado != LineaEstado.Diferida &&
                        (l.Planilla.Estado == PlanillaEstado.Borrador || l.Planilla.Estado == PlanillaEstado.ConAlertasCuenta ||
                         l.Planilla.Estado == PlanillaEstado.EnRevision || l.Planilla.Estado == PlanillaEstado.Observada))
            .Select(l => l.PlanillaId).Distinct().ToListAsync();
        var duenos = await DuenosAsync();
        foreach (var id in ids)
        {
            var p = await ciclos.PlanillaCompletaAsync(id, todasLasAreas: true);
            if (p is null) continue;
            var activas = p.Activas().ToList();
            foreach (var l in activas.Where(l => l.PrestadorId == prestadorId))
            {
                l.CuentaBancariaId = l.Prestador.Cuentas.FirstOrDefault(c => c.Vigente)?.Id;
                if (!l.ResultadoCuenta.EsAlerta()) continue;
                var cl = Clasificar(l, activas, duenos);
                l.AlertaCuentaResuelta = cl.Resultado == ResultadoCuenta.Coincide;
                if (l.AlertaCuentaResuelta) l.ResultadoCuentaDetalle = "Resuelta: cuenta registrada.";
            }
            ActualizarEstadoAlertas(p);
        }
    }

    /// <summary>R-26: correo al responsable con las filas, el resultado y qué corregir. Queda registrada y se puede reenviar.</summary>
    public async Task<SolicitudCorreccion> SolicitarCorreccionAsync(int planillaId, string para, string mensaje, IEnumerable<int>? lineaIds = null)
    {
        var p = await ciclos.PlanillaCompletaAsync(planillaId) ?? throw new ReglaException("Planilla no encontrada.");
        if (string.IsNullOrWhiteSpace(para)) throw new ReglaException("Indica el destinatario.");
        if (string.IsNullOrWhiteSpace(mensaje)) throw new ReglaException("Escribe el mensaje.");
        var seleccion = lineaIds?.ToHashSet();
        var filas = p.Activas().Where(l => l.AlertaAbierta && (seleccion is null || seleccion.Contains(l.Id))).OrderBy(l => l.Numero)
            .Select(l => new FilaSolicitud(l.Numero, l.Prestador.NombreCompleto, l.ResultadoCuenta.Nombre(), l.ResultadoCuentaDetalle ?? "")).ToList();
        if (filas.Count == 0) throw new ReglaException("No hay filas con alertas para solicitar corrección.");
        var s = new SolicitudCorreccion
        {
            PlanillaId = p.Id, Version = p.Version, EnviadaA = para.Trim(), EnviadaPor = usuario.Nombre, EnviadaEn = ciclos.AhoraUtc,
            Mensaje = mensaje.Trim(), Filas = Json.Serializar(filas)
        };
        db.SolicitudesCorreccion.Add(s);
        Enviar(p, s, filas);
        auditor.Registrar(nameof(SolicitudCorreccion), p.Id, "Solicitar corrección", $"{p.Titulo} v{p.Version}: filas {string.Join(", ", filas.Select(f => f.Fila))}");
        await db.SaveChangesAsync();
        return s;
    }

    public async Task ReenviarSolicitudAsync(int solicitudId)
    {
        var s = await db.SolicitudesCorreccion.FirstOrDefaultAsync(x => x.Id == solicitudId) ?? throw new ReglaException("Solicitud no encontrada.");
        var p = await ciclos.PlanillaCompletaAsync(s.PlanillaId) ?? throw new ReglaException("Planilla no encontrada.");
        s.Envios += 1;
        s.Estado = SolicitudEstado.Reenviada;
        Enviar(p, s, Json.Leer<List<FilaSolicitud>>(s.Filas) ?? []);
        auditor.Registrar(nameof(SolicitudCorreccion), p.Id, "Reenviar solicitud de corrección", $"Envío {s.Envios}");
        await db.SaveChangesAsync();
    }

    private void Enviar(Planilla p, SolicitudCorreccion s, List<FilaSolicitud> filas)
    {
        var cuerpo = s.Mensaje + "\n\n" + string.Join("\n", filas.Select(f => $"Fila {f.Fila} · {f.Prestador} · {f.Resultado} · {f.QueCorregir}"));
        correos.Encolar(ExtraerCorreo(s.EnviadaA), $"Corrección de cuentas: planilla {p.Titulo} v{s.Version} ({p.Ciclo.Codigo})", cuerpo);
    }

    private static string ExtraerCorreo(string para)
    {
        var a = para.IndexOf('<');
        var b = para.IndexOf('>');
        return a >= 0 && b > a ? para[(a + 1)..b] : para;
    }

    /// <summary>R-28: carga inicial por RUT desde planillas anteriores (I, O, P, Q). Repetible sin duplicar.</summary>
    public async Task<ResultadoCargaInicial> CargaInicialAsync(IEnumerable<FilaCuentaHistorica> filas, string origen)
    {
        var plan = CargaInicialReglas.Planificar(filas);
        var (bancos, tipos) = await CatalogosAsync();
        var prestadores = await db.Prestadores.Include(p => p.Cuentas).ToDictionaryAsync(p => p.Rut);
        var ahora = ciclos.AhoraUtc;
        int unicas = 0, varias = 0, omitidas = 0;
        var detalle = new List<string>(plan.Ignoradas);

        Prestador Prest(CuentaPropuesta c)
        {
            if (prestadores.TryGetValue(c.Rut, out var p)) return p;
            p = new Prestador { Rut = c.Rut, Dv = c.Dv, NombreCompleto = c.Nombre ?? "(sin nombre)" };
            db.Prestadores.Add(p);
            prestadores[c.Rut] = p;
            return p;
        }

        bool Agregar(CuentaPropuesta c, CuentaEstado estado, bool vigente, bool revision)
        {
            var banco = bancos.FirstOrDefault(b => b.Equals(c.Banco, StringComparison.OrdinalIgnoreCase));
            var tipo = tipos.FirstOrDefault(t => t.Equals(c.TipoCuenta, StringComparison.OrdinalIgnoreCase));
            if (banco is null || tipo is null) { detalle.Add($"{RutHelper.Formatear(c.Rut, c.Dv)}: banco o tipo fuera de catálogo ({c.Banco}/{c.TipoCuenta})."); omitidas++; return false; }
            var p = Prest(c);
            var norm = CuentaReglas.Normalizar(c.Cuenta);
            if (p.Cuentas.Any(x => x.Banco == banco && x.CuentaNormalizada == norm && x.TipoCuenta == tipo)) { omitidas++; return false; } // repetible sin duplicar
            if (vigente)
                foreach (var a in p.Cuentas.Where(x => x.Vigente)) { a.Vigente = false; a.Estado = CuentaEstado.Inactiva; a.ReemplazadaEn = ahora; }
            p.Cuentas.Add(new CuentaBancaria
            {
                Banco = banco, TipoCuenta = tipo, Cuenta = c.Cuenta, CuentaNormalizada = norm, Estado = estado, Vigente = vigente,
                Origen = CuentaOrigen.CargaInicial, OrigenDetalle = $"{origen} · planilla {Formato.Fecha(c.Fecha)}", RequiereRevision = revision,
                RegistradaPor = usuario.Nombre, RegistradaEn = ahora,
                ValidadaPor = estado == CuentaEstado.Validada ? usuario.Nombre : null, ValidadaEn = estado == CuentaEstado.Validada ? ahora : null,
                ReemplazadaEn = estado == CuentaEstado.Inactiva ? ahora : null
            });
            return true;
        }

        foreach (var c in plan.Unicas)
            if (Agregar(c, CuentaEstado.Validada, true, false)) unicas++;
        foreach (var grupo in plan.ConVarias.GroupBy(c => c.Rut))
        {
            var agrego = false;
            // Primero las inactivas, al final la más reciente como vigente (a revisión de Finanzas).
            foreach (var c in grupo.OrderBy(c => c.Vigente))
                agrego |= Agregar(c, c.Vigente ? CuentaEstado.PendienteValidacion : CuentaEstado.Inactiva, c.Vigente, c.Vigente);
            if (agrego) { varias++; detalle.Add($"{RutHelper.Formatear(grupo.Key, grupo.First().Dv)}: cuentas distintas; vigente la más reciente, a revisión de Finanzas."); }
        }
        foreach (var (banco, cuenta, ruts) in plan.CompartidasNoAsociadas)
            detalle.Add($"Cuenta {banco} {cuenta} aparece en {ruts.Count} RUT: no se asocia.");

        auditor.Registrar(nameof(CuentaBancaria), null, "Carga inicial de cuentas",
            $"{origen}: {unicas} únicas, {varias} con varias cuentas, {plan.CompartidasNoAsociadas.Count} compartidas, {omitidas} omitidas");
        await db.SaveChangesAsync();
        return new(unicas, varias, plan.CompartidasNoAsociadas.Count, omitidas, detalle);
    }

    /// <summary>Lee columnas I, J, O, P, Q de planillas anteriores en formato Finanzas.</summary>
    public static List<FilaCuentaHistorica> LeerHistoricas(Stream xlsx)
    {
        var a = ExcelPlanilla.LeerFormatoFinanzas(xlsx);
        var fecha = a.Encabezado?.FechaRecepcion ?? DateOnly.MinValue;
        return a.Filas.Where(f => !string.IsNullOrWhiteSpace(f.Cuenta))
            .Select(f => new FilaCuentaHistorica(f.Rut ?? "", f.Nombre, f.TipoCuenta ?? "", f.Cuenta ?? "", f.Banco ?? "", fecha)).ToList();
    }
}
