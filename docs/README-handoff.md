# Pago de Honorarios — Handoff para desarrollo

Solución: **IpsosPagoHonorarios** · Ipsos Chile · Bloque 1 del macroproceso *Pagos y Rendiciones*.
Mockup de referencia: `Pago de Honorarios.dc.html` (13 pantallas + versiones de 390 px), con `TopBar.dc.html` y `SideNav.dc.html` reutilizables.

> Datos ficticios. Lo marcado **TODO(diseño)** no está definido en la especificación y figura en *Preguntas abiertas*.

---

## 1. Alcance

Pago mensual de honorarios a toda persona que emite boleta de honorarios (encuestadores F2F, shoppers, supervisores, codificadores externos, etc.).

Incluye: carga de producción y armado de planillas por área; validación de cuentas bancarias; portal del prestador para subir la boleta (PDF del SII) y su conciliación; envío a Finanzas, revisión en cuatro grupos, devolución con plazo de 1 hora, diferimiento; aprobación, nómina, registro de transferencias, cierre y archivo.

Fuera de alcance: Bloque 2 (Rendiciones de Gastos) y Bloque 3 (Control Presupuestario por Job e Ítem). El modelo deja listos `Job`, `Area` y `Glosa`.

Flujo mensual:
1. Días 28–30: se descarga la producción y se arma una planilla por área (formato XLSX de Finanzas).
2. Al subir la planilla se compara cada cuenta con la registrada para ese RUT; con alertas, se pide corrección al responsable.
3. Cada prestador sube su boleta desde el portal; la app la lee y la concilia.
4. Operaciones envía a Finanzas, que revisa: personal, operativa, tributaria y bancaria.
5. Con observaciones, Finanzas devuelve con 1 hora de plazo; lo no corregido pasa al ciclo siguiente.
6. Finanzas aprueba, genera nómina, registra transferencias, cierra el ciclo y archiva respaldos.

Localización: español de Chile · RUT `12.345.678-5` en UI y `12345678-5` en planillas · montos `$1.234.567` · fechas `dd-mm-aaaa` · horas `HH:mm` · zona `America/Santiago`.

## 2. Stack (obligatorio)

| Capa | Tecnología |
|---|---|
| Runtime | .NET 10 (LTS), C# 14 |
| Web | ASP.NET Core Razor Pages (una página por pantalla) |
| UI | Bootstrap 5.3 + Bootstrap Icons 1.11 |
| Seguridad | ASP.NET Core Identity, roles en la misma base |
| Datos | EF Core 10 + SQL Server Express 2025, migraciones versionadas; pruebas con SQLite en memoria |
| PDF | UglyToad.PdfPig |
| Excel | ClosedXML (sobre la plantilla oficial) |
| Servidor | IIS + ASP.NET Core Hosting Bundle (Windows) |
| Publicación | Cloudflare Tunnel (cloudflared) + Cloudflare Access (Zero Trust gratuito) |
| Repositorio | GitHub · `IpsosPagoHonorarios` |

Estructura:
- `src/IpsosPagoHonorarios.Core` — entidades y reglas, sin NuGet.
- `src/IpsosPagoHonorarios.Web` — Razor Pages, EF Core, Identity, ClosedXML, PdfPig, `BackgroundService` de plazos, `TimeProvider` inyectado.
- `tests/IpsosPagoHonorarios.Tests` — xUnit.

### Sistema visual
El mockup usa el sistema "Ipsos Honorarios" de la sección 5 (navy #121B5A, teal #14A19E, IBM Plex Sans/Mono, radios 4/6 px), tomado del HTML de referencia entregado. Principios: badge con texto en todo estado, una tarea por pantalla, cuenta regresiva siempre visible en la barra superior, datos en fuente mono, montos a la derecha con tabular-nums, contraste AA, foco teal de 2 px, controles reales.

Mapeo de badges: Borrador, Diferido → neutral · En revisión → info · Observada, Corrección solicitada → warning · Plazo vencido, Rechazada, Cuenta de tercero → danger · Aprobada, Pagada, Cerrada, Coincide → success.
**TODO(diseño):** tono para estados no mapeados — Lista, Pendiente boleta, Corregida, Abierta (observación), Cuenta nueva, Posible error de tipeo, Distinta a la registrada, Con alertas de cuenta, Por transferir, Activado/Invitado/Sin invitar, Pendiente de validación, Inactiva. El mockup usa: info para Lista/Corregida/Cuenta nueva/Invitado; neutral para Pendiente boleta/Por transferir/Inactiva/Sin invitar; warning para Abierta/tipeo/Distinta/Con alertas/Pendiente de validación; success para Activado.

## 3. Roles y permisos

| Actividad | Operaciones | Finanzas | Prestador | Admin |
|---|---|---|---|---|
| Cargar producción o planilla y generar planilla | ✓ | lectura | — | ✓ |
| Resolver alertas de cuenta y solicitar corrección | ✓ | — | — | ✓ |
| Registrar cuenta nueva o cambio de cuenta | ✓ | ✓ | — | ✓ |
| Validar cuentas registradas | — | ✓ | — | ✓ |
| Carga inicial de cuentas | — | ✓ | — | ✓ |
| Enviar / reenviar planilla a Finanzas | ✓ | — | — | ✓ |
| Subir su propia boleta (solo sus filas) | — | — | ✓ | — |
| Seguimiento de boletas, recordatorios, confirmar lectura | ✓ | ✓ | — | ✓ |
| Cargar/reemplazar boleta en nombre del prestador (con motivo) | ✓ | — | — | ✓ |
| Revisar, observar, devolver con plazo, aprobar | — | ✓ | — | ✓ |
| Corregir observaciones | ✓ | — | — | ✓ |
| Aceptar o reabrir correcciones | — | ✓ | — | ✓ |
| Nómina, transferencias y cierre | — | ✓ | — | ✓ |
| Mantener Jobs, glosas y catálogos | lectura | ✓ | — | ✓ |
| Mantener prestadores (contacto y portal) | ✓ | ✓ | — | ✓ |
| Gestionar usuarios internos | — | — | — | ✓ |

El rol Prestador solo accede a `/Portal`; cualquier otra ruta → 403.

## 4. Acceso y publicación

- `honorarios.<dominio>` — equipo interno: Cloudflare Access + Identity.
- `boletas.<dominio>` — prestadores: sin Access (el plan gratuito cubre hasta 50 usuarios); Identity + límite de intentos + Turnstile opcional. Solo sirve `/Portal`, `/Cuenta` y estáticos.
- Ingreso: interno con correo; prestador con RUT (UserName = RUT) y contraseña. Sin segundo factor.
- Bloqueo: 5 intentos fallidos → 15 minutos.
- Activación y recuperación solo por correo: enlace de un solo uso, 72 h. Sin SMS. Sin correo, Operaciones restablece o copia el enlace y lo entrega por su canal; queda en bitácora.

## 5. Pantallas (Razor Pages)

| # | Ruta | Rol | Artboard |
|---|---|---|---|
| 1 | /Cuenta/Login, /Cuenta/Activar, /Cuenta/Recuperar | Todos | `#s01` |
| 2 | /Index | Interno | `#s02`, `#s02m` (390 px) |
| 3 | /Ciclos/Produccion | Operaciones | `#s03` |
| 4 | /Ciclos/ValidacionCuentas | Operaciones / Finanzas | `#s04` |
| 5 | /Ciclos/Planilla | Operaciones edita / Finanzas lee | `#s05` |
| 6 | /Boletas/Seguimiento | Operaciones / Finanzas | `#s06` |
| 7 | /Ciclos/Correcciones | Operaciones / Finanzas | `#s07` |
| 8 | /Finanzas/Revision | Finanzas | `#s08` |
| 9 | /Finanzas/Pagos | Finanzas | `#s09` |
| 10 | /Maestros/Prestadores | Operaciones / Finanzas | `#s10` |
| 11 | /Maestros/Jobs | Finanzas | `#s11` |
| 12 | /Ciclos/Historial | Interno | `#s12` |
| 13 | /Portal | Prestador | `#s13` (390 px) |

Estructura común: barra superior (app, selector de ciclo, píldora "Corrección vence en mm:ss" con devolución activa, usuario, Salir); menú lateral agrupado *Ciclo de pago · Finanzas · Maestros · Consulta* que en celular se vuelve fila de chips; encabezado con título, ruta, rol y badge de estado.

Interacciones del mockup: cuenta regresiva en vivo; prop **simularVencido** (Tweaks) muestra el estado tras el vencimiento (R-12); pestañas por área en Planilla; selección de fila en Seguimiento y Revisión; validación de RUT en vivo en Prestadores; carga simulada de boleta en el Portal.

## 6. Modelo de datos

Todas las entidades: `CreadoEn/Por`, `ModificadoEn/Por`. Montos `decimal(18,0)`, valor unitario `decimal(18,2)`, cantidad `decimal(18,4)`.

- **Ciclo**: Periodo (día 1 del mes, único), Codigo (`OCT-2026`), Estado (Abierto/Cerrado), FechaPagoProgramada (día 5 del mes siguiente, configurable), CerradoEn/Por.
- **Planilla** (única por ciclo y área): FechaRecepcion, ResponsableNombre, ResponsableEmail, Estado, Version.
- **Prestador**: Rut + Dv (únicos), NombreCompleto, Email?, Telefono? (al menos uno), UsuarioId (UserName = RUT), PortalInvitadoEn/ActivadoEn, Activo.
- **CuentaBancaria**: Prestador (titular = su RUT), Banco, TipoCuenta, Cuenta, CuentaNormalizada (solo dígitos, sin ceros a la izquierda), Estado (PendienteValidacion → Validada → Inactiva; Rechazada), Origen (CargaInicial/Registro) + detalle, RegistradaPor/En, ValidadaPor/En, ReemplazadaEn. Una sola vigente por prestador; índice (Banco, CuentaNormalizada); nunca se borran.
- **Catálogos**: Area (Nombre, CodigoArea) · Glosa (NombreGlosa, Item, CuentaContable) · Banco (Nombre, CodigoBanco) · TipoCuenta (Nombre, Codigo) · TipoGasto (Costo Directo, Payroll). Nombres exactos al catálogo de Finanzas.
- **Job**: JobBookNumber (12 dígitos, único), Nombre, AreaSugerida, ValorUnitarioSugerido, Activo.
- **LineaPago**: Numero, TipoGasto, Job, Glosa, ValorUnitarioBruto, Cantidad, ValorTotalBruto, Prestador, CuentaPlanilla (tipo, número, banco tal como vinieron), CuentaBancariaId, ResultadoCuenta, AlertaCuentaResuelta, Estado, DiferidaDesde/ACicloId.
- **BoletaHonorarios**: Planilla, Prestador, Canal (Portal/Operaciones), MotivoCargaOperaciones, SubidaPor/En, Estado (Recibida, Confirmada, Observada, Reemplazada, Rechazada), NumeroBoleta, RutEmisor/Dv, NombreEmisor, RutReceptor, FechaEmision, MontoBruto, MontoRetencion, MontoLiquido, RutaPdf, HashPdf (SHA-256), TextoExtraido, Confianza, ConfirmadaPor/En, ReemplazaABoletaId, ResultadoValidacion.
- **Observacion**: LineaPago, Tipo (Error de digitación, Datos incompletos, Falta de boleta, Diferencia entre montos, Inconsistencia numérica, Datos bancarios incorrectos), Campo, Detalle, Estado (Abierta, Corregida, Aceptada, Vencida), CreadaPor/En, CorregidaPor/En, Respuesta, Devolucion.
- **Devolucion**: Planilla, Version, DevueltaEn/Por, VenceEn (= +60 min), ReenviadaEn, Resultado (Pendiente, ReenviadaATiempo, Vencida).
- **SolicitudCorreccion**: Planilla, Version, EnviadaA, EnviadaPor/En, Mensaje, Filas (JSON), Estado.
- **Transferencia** (una por boleta): Planilla, Prestador, Boleta, CuentaBancaria, Fecha, NumeroOperacion, MontoLiquido, Comprobante.
- **Auditoria**: Fecha, Usuario, Entidad, Id, Accion, Detalle.
- **Parametro**: TasaRetencion (2026 = 0,1525), PlazoCorreccionMinutos (60), RutEmpresa, RazonSocialEmpresa, días 28–30 de descarga, día de pago.

## 7. Estados

**Planilla:** `Borrador → ConAlertasCuenta ⇄ Borrador → EnRevision → Observada → EnRevision` (reenvío a tiempo, o plazo vencido con diferimiento) `→ Aprobada → EnPago → Cerrada`.

**Línea:** `PendienteBoleta → Lista → Observada → Corregida → Lista → Aprobada → Pagada`. Desde PendienteBoleta u Observada → `Diferida` (se copia al ciclo siguiente).

**CuentaBancaria:** `PendienteValidacion → Validada → Inactiva`; `Rechazada`.
**Boleta:** Recibida, Confirmada, Observada, Reemplazada, Rechazada.
**Observación:** Abierta, Corregida, Aceptada, Vencida.
**Devolución:** Pendiente, ReenviadaATiempo, Vencida.
**TODO(diseño):** estados de `SolicitudCorreccion` no enumerados.

## 8. Reglas de negocio

| ID | Regla |
|---|---|
| R-01 | RUT válido por módulo 11 (DV 0-9 o K). Se acepta con o sin puntos y guion. |
| R-02 | Producción se carga entre los días 28 y 30; fuera de esa ventana, con aviso. Se rechaza la fila (con N° y motivo) si el RUT es inválido, el Job no tiene 12 dígitos, la glosa no está en catálogo, o valor unitario o cantidad ≤ 0. Si RUT o Job no existen, se crean y se avisa. Un archivo con errores no se carga. |
| R-03 | Valor total bruto = ROUND(valor unitario × cantidad, 0), redondeo tipo Excel (lejos de cero). Cantidad admite decimales. Valor unitario sugerido desde el Job. |
| R-04 | El valor de la planilla es el bruto de la boleta; se transfiere el líquido. (Supuesto a confirmar con Finanzas.) |
| R-05 | Retención = ROUND(bruto × tasa). Tasa 2026 = 15,25 %, configurable por año. Líquido = bruto − retención. Por boleta, no por línea. |
| R-06 | Una boleta por prestador y planilla, por el total de sus filas (N° repetido en todas). Prestador en dos áreas → una boleta por planilla. Exige: RUT emisor = prestador; RUT receptor = empresa; fecha entre el día 1 del período y el día 10 del mes siguiente; bruto = suma de filas (tolerancia $0); no anulada; (RUT emisor, N°) sin uso previo. Una segunda boleta reemplaza a la anterior (Reemplazada). |
| R-07 | Una cuenta vigente por prestador, a su nombre. Banco y tipo en catálogo. Tipo RUT ⇒ banco ESTADO y cuenta = RUT sin DV. Solo dígitos, 6 a 20, ingresada dos veces. No se registra una cuenta (banco + normalizada) de otro RUT. |
| R-08 | Validación preliminar al generar, al cargar boletas y antes de enviar: R-01, R-03, R-06, R-07, R-24. Solo se envía sin alertas de cuenta y con boleta de todos, o con esas filas diferidas. |
| R-09 | Revisión de Finanzas por prestador: Personal (RUT, nombre), Operativa (Job, glosa, valor = cantidad × unitario), Tributaria (boleta presente y monto = suma), Bancaria (cuenta coincide, validada, titular = prestador). Cada observación: tipo, campo, detalle. |
| R-10 | Devolución requiere ≥ 1 observación abierta. Crea Devolución con vencimiento a 60 min, deja la planilla Observada, avisa por correo al responsable y a prestadores con problemas de boleta, y muestra cuenta regresiva. |
| R-11 | Reenvío antes del vencimiento: Devolución "a tiempo", versión +1, se archiva el XLSX de esa versión. |
| R-12 | Proceso en segundo plano cada minuto. Al vencer: filas de prestadores con observaciones abiertas → Diferida y se copian a la planilla de la misma área del ciclo siguiente (se crea si no existe). Observaciones → Vencida. El resto sigue en revisión. |
| R-13 | Aprobación solo sin observaciones abiertas ni correcciones sin revisar, con cuentas validadas y boletas cuadradas. Congela la planilla y confirma las boletas. |
| R-14 | Pago: nómina XLSX con la cuenta registrada; se registran fecha, N° de operación y comprobante; líneas → Pagada. |
| R-15 | Cierre solo con todas las líneas pagadas o diferidas. ZIP con planillas de cada versión, boletas, nómina, comprobantes y bitácora. Luego, solo lectura. |
| R-16 | Auditoría de todo cambio de estado, cambio bancario, carga de archivo, observación y acceso al portal. |
| R-17 | El prestador ve solo sus filas (filtro en el servicio) y solo sube o reemplaza su boleta. No edita montos ni datos bancarios. |
| R-18 | Puede subir boleta desde que la planilla existe hasta que se aprueba, solo para filas pendientes o con boleta observada. |
| R-19 | Validación inmediata: rechazo si RUT emisor ≠ prestador ("La boleta debe estar emitida por usted"). Monto distinto → se acepta mostrando la diferencia. PDF real (`%PDF-`), ≤ 2 MB, no duplicado (hash). |
| R-20 | Cuentas del portal: correo o teléfono (al menos uno). Usuario = RUT. Solo contraseña. Activación/recuperación solo por correo (72 h). Sin correo, Operaciones entrega el enlace. Sin SMS. |
| R-21 | Avisos solo por correo: al generarse su pago, al observarse su boleta (con plazo), cuando Operaciones carga una boleta en su nombre y al pagarse. Recordatorio manual a pendientes. |
| R-22 | Carga en nombre del prestador: Operaciones, motivo obligatorio, mismas validaciones de R-19. Canal Operaciones, bitácora y aviso al prestador. Finanzas ve cuáles se cargaron así. |
| R-23 | Registro/cambio de cuenta por Operaciones o Finanzas, sin respaldo documental. Queda pendiente; solo Finanzas valida. El cambio deja la anterior Inactiva. Solo se aprueba y paga una línea con cuenta validada. |
| R-24 | Validación al subir la planilla (cuenta normalizada vs. vigente del RUT): Coincide · Posible error de tipeo (mismo banco y tipo con Damerau-Levenshtein ≤ 2; o tipo RUT con cuenta ≠ RUT; o filas del mismo prestador con cuentas distintas) · Cuenta nueva (sin cuenta registrada) · Distinta a la registrada (otro banco o tipo, o muy distinta) · Cuenta de un tercero (de otro RUT; se bloquea) · Sin cuenta en la planilla (se usa la registrada, sin alerta). |
| R-25 | Con alertas abiertas la planilla queda ConAlertasCuenta y no se envía. Se resuelve: nueva versión corregida (validada completa), registro de cuenta (R-23) o diferir la fila. |
| R-26 | Solicitud de corrección: correo al responsable con filas, resultado y qué corregir. Registrada y reenviable. |
| R-27 | Siempre se paga a la cuenta registrada y validada. Nómina y columnas O, P, Q del XLSX exportado usan la cuenta registrada. |
| R-28 | Carga inicial por RUT desde planillas anteriores (columnas I, O, P, Q). Cuenta única → Validada vigente. Cuentas distintas → la más reciente vigente, demás Inactiva, caso a revisión de Finanzas. Misma cuenta en varios RUT → no se asocia. Repetible sin duplicar. |

## 9. Validaciones (resumen por formulario)

- **Login:** "Correo o RUT" + contraseña; bloqueo 5/15 min (§4).
- **RUT** (todas las pantallas): R-01, validación en vivo; normalizar a `12345678-5` al guardar.
- **Producción:** R-02 por fila (RUT, Job 12 dígitos, glosa en catálogo, valor y cantidad > 0); archivo XLSX o CSV; área, tipo de gasto, responsable y correo obligatorios. TODO(diseño): obligatoriedad/forma del correo del responsable.
- **Cuenta bancaria:** R-07 (catálogo, regla tipo RUT, 6–20 dígitos, doble ingreso, no de tercero).
- **Prestador:** nombre, RUT, al menos correo o teléfono (R-20).
- **Boleta:** R-19 + R-06 + R-18.
- **Observación:** línea, tipo (6 valores), campo y detalle obligatorios.
- **Carga en nombre:** motivo obligatorio (R-22).
- **Registrar pago:** fecha, N° de operación y comprobante PDF (R-14).
- **Lectura de boleta (PdfPig):** firma PDF, ≤ 2 MB, hash; texto por líneas según posición vertical; normalizar a mayúsculas sin tildes; regex configurables: N° (`BOLETA DE HONORARIOS ELECTRONICA … N° (\d+)`), RUT emisor = primer RUT, receptor = siguiente distinto, fecha ("31 de Octubre de 2026" o dd/mm/aaaa), bruto ("Total Honorarios"), retención ("Impto. Retenido" o "Retención", ignorando %), líquido ("Total"). Confianza: Alta (todos los campos y bruto − retención = líquido), Media (falta un campo no monto), Baja (otro caso). Operaciones/Finanzas pueden corregir la lectura y confirmar.

## 10. Formato Excel de Finanzas

Hoja **Planilla**, una por área, escrita sobre la plantilla oficial conservando fórmulas, listas desplegables (`banco`, `tipos_de_cuenta`) y la hoja **Formato** con catálogos.

Encabezado: `B1` Fecha de Recepción (dd/mm/yyyy) · `B2` Nombre del Responsable · `B3` Monto Total a pago `=SUM(H9:H3007)` · `B4` Área Responsable · `B5` Código Área (VLOOKUP).
Fila 8: títulos. Datos desde la fila 9 hasta la primera fila con Rut vacío.

| Col | Título | Notas |
|---|---|---|
| A | Tipo de Gasto | Costo Directo / Payroll |
| B | Job Book Number | 12 dígitos |
| C | Job Book Number Name | |
| D | Nombre Glosa | exacto al catálogo |
| E | Item | fórmula (VLOOKUP de D) |
| F | Valor unitario bruto | |
| G | Cantidad | admite decimales |
| H | Valor total bruto | `ROUND(F*G,0)` |
| I | Rut | `12345678-5` |
| J | Nombres | completo |
| K | N° boleta | se repite en todas las filas del prestador |
| L–N | Responsable, Área, Código | fórmulas que copian el encabezado |
| O | Tipo Cuenta | CORRIENTE, VISTA, RUT, AHORRO, CHEQUERA ELECTRONICA, DEBITO |
| P | Cuenta | texto si tiene ceros a la izquierda |
| Q | Banco | ESTADO, CHILE, BANEFE, BCI, SANTANDER, SCOTIABANK, ITAU, FALABELLA, MERCADO PAGO, Tenpo… |

Nombre de archivo: `Planilla honorarios OCTUBRE_2026 - FACE TO FACE v2.xlsx`.
Exportar e importar el mismo archivo no debe producir diferencias; B3 = suma de H.

Catálogos (extracto): Áreas FACE TO FACE 21183 · MYSTERY SHOPPING 21187 · DATA PROCESSING 21184 · Operations CATI 21161 (+16). Glosas: Honorarios entrevistadores 1310/602101 · Honorarios supervisión 1330/602101 · Honorario codificación externa 2830/602101 (+28). Bancos: ESTADO 12 · CHILE 1 · BANEFE 37 · BCI 16 · SANTANDER 37 · MERCADO PAGO 874 · Tenpo 730.
Semilla de catálogos desde CSV. Nunca datos personales reales.

## 11. Datos de demo (solo Development)

Ciclo OCT-2026, pago 05-11-2026. Datos de la sección 11 de la especificación. Para cuadrar los totales dados, el mockup completó con datos ficticios (marcar como demo, no como regla):
- FACE TO FACE (10 líneas, 315 u., $2.161.600): además de los 5 prestadores dados, Agustín Flores 40 × $6.500; Tomás Herrera Lagos 18.456.123-9 45 × $6.500; Catalina Reyes Vidal 16.543.210-K 35 × $7.200; Benjamín Castro Núñez 19.876.543-0 28 × $7.450 (Job 260044200110). Los 6 pagos aprobados (sin Valentina ni Francisca) dan bruto $1.650.100, retención $251.641 y líquido $1.398.459, como indica la especificación.
- DATA PROCESSING (9 filas, $219.564): cantidades 85,28 · 102,4 · 120,5 · 96,75 · 110 · 88,36 · 130,12 · 45 · 99,845 (Paula e Ignacio con 2 filas).
- MYSTERY SHOPPING (4 líneas, $532.000): 3 prestadores ficticios a $14.000; glosa TODO(diseño).
- Nombres de Jobs, cuentas de los prestadores F2F, usuarios internos y ciclos anteriores: ficticios.
- RUT empresa usado en el mockup: `77.777.777-7` (ficticio).

## 12. Despliegue

1. Windows Server con IIS + ASP.NET Core Hosting Bundle (.NET 10).
2. SQL Server Express 2025; `dotnet ef database update` con migraciones versionadas.
3. `cloudflared` como servicio, con dos hostnames sobre la misma app:
   - `honorarios.<dominio>` → Cloudflare Access (equipo interno, ≤ 50 usuarios) + Identity.
   - `boletas.<dominio>` → sin Access; middleware que solo permite `/Portal`, `/Cuenta` y estáticos; rate limiting en login; Turnstile opcional.
4. Correo saliente para activación, recuperación y avisos (R-20, R-21). **TODO(diseño):** proveedor SMTP.
5. Almacenamiento de PDFs y ZIPs de respaldo. **TODO(diseño):** ruta, retención y respaldo.
6. GitHub Actions: build + test en cada push; publicar en `main` solo si todo pasa.

## 13. Plan por hitos

Cada hito termina con `dotnet build` y `dotnet test` en verde.

1. **Base:** solución, Core sin dependencias, Identity con roles, catálogos desde CSV, auditoría, `TimeProvider`, CI. Reglas R-01, R-03, R-05, R-16.
2. **Producción y planilla:** importación XLSX/CSV, generación y exportación del formato Finanzas (ClosedXML), round-trip. R-02, R-08, R-27.
3. **Cuentas:** registro, validación, carga inicial, validación al subir planilla, solicitud de corrección. R-07, R-23, R-24, R-25, R-26, R-28.
4. **Portal y boletas:** cuentas del portal, subida y lectura PdfPig, conciliación, carga en nombre, avisos. R-06, R-17–R-22.
5. **Revisión y correcciones:** observaciones, devolución, `BackgroundService` de plazos, diferimiento. R-09–R-13.
6. **Pagos y cierre:** nómina, transferencias, cierre y ZIP, historial. R-14, R-15.
7. **Publicación:** IIS, Cloudflare Tunnel/Access, restricción por host, Turnstile.

Pruebas mínimas: RUT y montos (incluido ROUND(250 × 85,28) = 21.320); 6 resultados de R-24 con el caso DATA PROCESSING (2 coincide, 2 tipeo, 1 nueva, 1 distinta, 1 tercero); R-28 con sus 3 casos; conciliación con varias filas ($345.000); fecha límite día 10 del mes siguiente; plazo de 60 min; aprobación bloqueada con observaciones o cuentas sin validar; el Prestador no ve ni sube boletas de otro RUT; round-trip XLSX sin diferencias y B3 = Σ H.

## 14. Preguntas abiertas

1. Confirmar supuesto bruto vs. líquido (R-04).
2. Formato de la nómina para la carga masiva del banco.
3. Día de pago del mes (hoy: día 5 del mes siguiente, configurable).
4. PDFs de boletas reales anonimizados para afinar la lectura.
5. Formato de la exportación del sistema de encuestas.
6. Logo oficial (el mockup usa un monograma provisorio).
8. TODO(diseño): tonos de badge para estados no mapeados (ver §2).
9. TODO(diseño): RUT y razón social de la empresa receptora (Parámetro).
10. TODO(diseño): BANEFE y SANTANDER figuran con el mismo código 37.
11. TODO(diseño): glosa y detalle de las líneas de MYSTERY SHOPPING.
12. TODO(diseño): estados de SolicitudCorreccion.
13. TODO(diseño): ¿el reenvío exige 0 observaciones abiertas o permite reenviar con algunas pendientes?
14. TODO(diseño): política de contraseña del portal.
15. TODO(diseño): cómo se marca una boleta "anulada" (R-06) — fuente del dato.
16. TODO(diseño): umbral de "cuenta muy distinta" para *Distinta a la registrada* cuando banco y tipo coinciden (R-24).
17. TODO(diseño): proveedor de correo y almacenamiento de archivos.
