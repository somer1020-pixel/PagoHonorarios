# Pruebas de seguridad

Fecha: 4 de octubre de 2026. Objetivo: inyección SQL y los controles de acceso relacionados, sobre la aplicación
publicada en modo Production contra SQL Server 2022, con datos de prueba (3.000 prestadores, 20 áreas, ciclos
cerrados y el ciclo abierto). Las pruebas son contra una base **de prueba**, nunca la productiva.

## Resumen

No se encontraron vulnerabilidades. La inyección SQL no es posible: todo el acceso a datos pasa por EF Core e
Identity, que consultan con parámetros. Los controles de acceso (por rol, por área y del portal de prestadores)
resisten los intentos de salto. Quedan recomendaciones de endurecimiento (cabeceras HTTP y confianza en el proxy),
que no son fallas explotables hoy pero conviene aplicar antes de salir a producción.

## 1. Inyección SQL — sin hallazgos

Se probaron cargas típicas (`' OR '1'='1`, `' OR 1=1--`, `admin'--`, `'; WAITFOR DELAY '0:0:5'--`,
`' UNION SELECT NULL--`) en:

- el **ingreso** (usuario y contraseña),
- la **búsqueda de prestadores** (Maestros → Prestadores),
- los **parámetros de la URL** (`?Planilla=`, `?cicloId=`, etc.).

Resultado: ninguna carga inició sesión, ninguna devolvió filas de más y la de `WAITFOR DELAY` respondió al instante
(no se ejecutó). Las cargas en parámetros numéricos simplemente no enlazan y la página responde normal.

Por qué: la aplicación no arma SQL concatenando texto del usuario. Todo el acceso a datos usa consultas LINQ de
EF Core (que viajan como consultas con parámetros) y el inicio de sesión usa `UserManager` de ASP.NET Identity. El
único SQL escrito a mano es:

- el bloqueo de una planilla, con `ExecuteSqlInterpolatedAsync($"UPDATE Planillas SET Version = Version WHERE Id =
  {planillaId}")`, que EF Core convierte en un parámetro (no en concatenación), y
- dos consultas fijas de administración de la base (estado de `READ_COMMITTED_SNAPSHOT`), sin datos del usuario.

## 2. Autenticación y sesión

- **Bloqueo de cuenta:** 5 intentos fallidos bloquean 15 minutos (Identity).
- **Límite por IP:** el ingreso y la recuperación están limitados a 10 POST por minuto por IP (responde HTTP 429).
- **Antifalsificación (CSRF):** un POST sin el token `__RequestVerificationToken` recibe HTTP 400. Verificado en el
  cierre de sesión.
- **Cookies:** la cookie de sesión es `HttpOnly`; la de antifalsificación es `SameSite=Strict`. En producción
  (HTTPS) ambas viajan como `Secure`.
- **Contraseñas:** se almacenan con el hash PBKDF2 de Identity.

## 3. Control de acceso — sin hallazgos

Todos los intentos de ver datos de otra persona o de otra área fueron bloqueados. Casos verificados en vivo:

| Prueba | Esperado | Resultado |
| --- | --- | --- |
| Operativo del área 1 abre una planilla del área 2 | No la ve | Sí (no carga la ajena) |
| Operativo del área 1 descarga el **Excel** de una planilla del área 2 | 404 | 404 (la propia: 200) |
| Operativo del área 1 descarga el **PDF de una boleta** del área 2 | 404 | 404 (misma área: 200) |
| Operativo entra a Finanzas o Maestros | 403 | 403 |
| Prestador entra a cualquier página interna | 403 | 403 |
| Prestador sube una boleta a una planilla donde no tiene filas | Rechazo por regla | "No tienes filas…", sin error |
| Operativo descarga el ZIP de cierre (es de Finanzas/Admin) | 403 | 403 |

Detalle importante del diseño: el filtro por área está definido sobre la planilla y **se propaga** a sus boletas,
líneas y observaciones porque son navegaciones requeridas. Se confirmó de punta a punta: con una boleta real subida a
un área, el operativo de esa área la descarga (200) y el operativo de otra área recibe 404. La identidad del
prestador en el portal se toma de la sesión, no del formulario, así que nadie puede subir una boleta "a nombre de"
otro.

## 4. Recomendaciones de endurecimiento

No son fallas explotables hoy, pero conviene aplicarlas antes de producción:

1. **Confianza en el proxy (lo más relevante).** La aplicación acepta el encabezado `X-Forwarded-For` de cualquier
   origen (`KnownProxies` vacío). Como el límite de ingresos es por IP, alguien podría enviar una IP distinta en cada
   intento y esquivarlo. Hay que configurar `KnownProxies` con la IP real del Cloudflare Tunnel / IIS. (También está
   en `docs/pruebas-de-carga.md`.)
2. **Cabeceras de seguridad.** La aplicación no envía `Content-Security-Policy`, `X-Content-Type-Options: nosniff`,
   `Referrer-Policy` ni `Permissions-Policy`. Las vistas Razor ya escapan el HTML (no hay `Html.Raw` con datos del
   usuario), así que no hay XSS hoy, pero estas cabeceras agregan defensa en profundidad. Conviene un middleware que
   las agregue; la `Content-Security-Policy` hay que probarla contra la interfaz para no romper estilos o scripts en
   línea.
3. **HTTPS obligatorio en producción.** Para que la cookie de sesión viaje siempre como `Secure`, el sitio debe
   servirse solo por HTTPS (detrás de Cloudflare, forzar HTTPS). `UseHsts` ya está activo fuera de desarrollo.
4. **Límite de ingresos y NAT.** 10 intentos por minuto por IP puede afectar a oficinas que salen por una sola IP.
   Revisar el valor `Publicacion:IntentosPorMinuto` según el caso.
5. **Encabezado `Server: Kestrel`.** Expone el servidor; se puede ocultar (`AddServerHeader = false`). Es menor.
6. **Política de contraseña del portal.** Hoy es el mínimo provisorio (8 caracteres con un dígito, marcado como TODO
   en el código). Conviene alinearla con la política de la empresa y evaluar segundo factor para los perfiles
   internos.

## Cómo se repiten estas pruebas

Los scripts de prueba usados están en el entorno de desarrollo (no se versionan porque llevan credenciales de
prueba). Cada prueba: inicia sesión por HTTP con el token antifalsificación, envía las cargas y compara el código de
respuesta, el tiempo y el contenido. Para repetirlas basta la aplicación publicada contra una base de prueba y las
credenciales del escenario de carga (`tests/IpsosPagoHonorarios.Carga`).
