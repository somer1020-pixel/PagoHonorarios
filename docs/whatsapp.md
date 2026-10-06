# Avisos por WhatsApp

La aplicación puede avisar por WhatsApp a los prestadores cuando deben subir su boleta. Usa la **API de WhatsApp
Business (Cloud API de Meta)**. WhatsApp solo permite iniciar una conversación con **plantillas** aprobadas
previamente por Meta, y solo con personas que dieron su consentimiento; la aplicación respeta ambas reglas.

## Cuándo se envía

| Aviso | Cuándo | Plantilla |
| --- | --- | --- |
| Solicitud de boleta | Al importar una planilla de producción: el pago del prestador está listo para boletear | `solicitud_boleta` |
| Recordatorio | Cuando Operaciones o Finanzas usa **Recordar** en Seguimiento de boletas (solo a quien aún no sube su boleta) | `recordatorio_boleta` |
| Boleta observada | Al pedir una nueva boleta o al devolver la planilla con observaciones de boleta | `boleta_observada` |

Los correos siguen enviándose como antes. WhatsApp es un canal adicional.

Un mensaje solo se envía si se cumplen **todas** estas condiciones:

1. El canal está activado (`WhatsApp:Habilitado = true`) y tiene credenciales.
2. El prestador **autorizó** recibir WhatsApp (ver más abajo).
3. Su teléfono es un **celular** válido: chileno (`9 1234 5678`, `+56 9 1234 5678`) o internacional con `+`. Los fijos no tienen WhatsApp.
4. No se envió el mismo aviso (mismos datos) a ese número en las últimas 24 horas. Así, reimportar una planilla no repite el mensaje.

Si Meta no puede entregar el mensaje por una falla transitoria (red, límite de velocidad, error 5xx), la aplicación
reintenta a los 2 y a los 10 minutos (3 intentos en total). Los errores definitivos (plantilla inexistente, número sin
WhatsApp, token inválido) no se reintentan y quedan registrados con el motivo.

## Consentimiento del prestador

WhatsApp exige consentimiento previo. Hay dos formas de registrarlo, y ambas guardan la fecha:

- **El propio prestador**, en el portal (tarjeta **Avisos por WhatsApp**): indica su celular y marca la casilla. Es la
  forma recomendada, porque queda su consentimiento explícito. Puede retirarlo cuando quiera.
- **Operaciones o Finanzas**, en **Maestros → Prestadores**, con la casilla "El prestador autorizó recibir avisos por
  WhatsApp", si el prestador lo pidió por otro medio.

La tarjeta del portal solo aparece cuando el canal está activado.

## Puesta en marcha

1. **Cuenta de Meta.** Cuenta de Meta Business (idealmente con el negocio verificado) y una app en
   [Meta for Developers](https://developers.facebook.com) con el producto **WhatsApp**. Asocia un número de teléfono
   de WhatsApp Business. Anota el **Phone number ID** (es un identificador, no el número).
2. **Token permanente.** El token de prueba que muestra Meta vence en 24 horas. Crea un **usuario del sistema** en
   Business Settings, asígnale la app y genera un token con el permiso `whatsapp_business_messaging`.
3. **Plantillas.** En WhatsApp Manager crea las tres plantillas siguientes, categoría **Utilidad**, idioma
   **Español** (si eliges un idioma distinto, como `es_CL`, cámbialo en `WhatsApp:Idioma`). Los nombres deben ser
   exactamente estos, y las variables, en este orden:

   **`solicitud_boleta`**
   ```
   Hola {{1}}, tu pago del ciclo {{2}} ya está listo para boletear. Emite una sola boleta de honorarios por {{3}} bruto y súbela en el portal de boletas antes del {{4}}. Gracias.
   ```
   Variables: 1 nombre · 2 ciclo (OCT-2026) · 3 monto bruto ($331.500) · 4 fecha límite (10-11-2026).

   **`recordatorio_boleta`**
   ```
   Hola {{1}}, aún no recibimos tu boleta de honorarios del ciclo {{2}}. Súbela en el portal de boletas antes del {{3}} para que podamos pagarte a tiempo. Gracias.
   ```
   Variables: 1 nombre · 2 ciclo · 3 fecha límite.

   **`boleta_observada`**
   ```
   Hola {{1}}, revisamos tu boleta y tiene una observación: {{2}}. Por favor sube una nueva boleta en el portal de boletas {{3}}. Gracias.
   ```
   Variables: 1 nombre · 2 motivo de la observación · 3 plazo (“antes de las 22:52” o “a la brevedad”).

   Conviene agregar al texto la dirección del portal de prestadores (por ejemplo, "Ingresa en https://boletas.&lt;dominio&gt;")
   o un botón con ese enlace. Al crear cada plantilla, Meta pide un ejemplo para cada variable. Las aprobaciones
   suelen tardar de minutos a un par de días.
4. **Configuración del servidor.** En `appsettings.Production.json` (ignorado por git) o en variables de entorno
   (`WhatsApp__AccessToken`, etc.). **El token es un secreto: nunca al repositorio.** Ejemplo en
   `deploy/appsettings.Production.ejemplo.json`:

   ```json
   "WhatsApp": { "Habilitado": true, "PhoneNumberId": "<id>", "AccessToken": "<token>", "Idioma": "es" }
   ```

   Otras opciones: `ApiVersion` (por defecto `v21.0`), `MaxIntentos` (3) y los nombres de plantilla
   `PlantillaSolicitud`, `PlantillaRecordatorio` y `PlantillaObservada`, si usas otros nombres.
5. **Reiniciar la aplicación** y entrar como Administrador a **Maestros → Parámetros → Avisos por WhatsApp**. Debe decir
   *Activado*. Usa **Enviar prueba** con tu celular: envía la plantilla `solicitud_boleta` con datos ficticios y
   muestra el resultado o el error de Meta. Hazlo antes de pedir a los prestadores que autoricen.
6. **Base de datos.** La aplicación crea la tabla `WhatsApp` y la columna de consentimiento al iniciar si tiene permiso
   `db_ddladmin`. Si no, un DBA ejecuta `database/09_WhatsApp.sql`.

## Seguimiento

**Maestros → Parámetros → Avisos por WhatsApp** muestra los mensajes en cola, los enviados en las últimas 24 horas, los
fallidos y el último error. Cada mensaje queda en la tabla `WhatsApp` con su estado, intentos, error y el identificador
del mensaje en WhatsApp.

Errores frecuentes de Meta: `132001` la plantilla no existe con ese nombre o idioma · `132000` el número de variables
no coincide · `131026` el número no tiene WhatsApp · `190` el token venció o es inválido.

## Consideraciones

- **Costo.** Meta cobra por mensaje según su tarifa vigente y la categoría de la plantilla. Revisa la tarifa para Chile
  antes de activar el canal.
- **Límites.** Meta limita cuántos destinatarios distintos puede recibir un número por día, y el límite parte bajo y
  sube con el uso y la verificación del negocio. Un día de punta con muchos prestadores puede superarlo: los mensajes
  excedidos fallan con error, quedan registrados y los prestadores igual reciben el correo.
- **Calidad del número.** Si los prestadores bloquean o reportan los mensajes, Meta puede reducir el límite del número.
  Por eso solo se escribe a quien autorizó y solo con avisos de la boleta.
- **Pruebas.** El envío se probó con pruebas automáticas y contra un servidor que imita la API de Meta (formato de la
  solicitud, reintentos y errores). La primera conexión con Meta real se valida con **Enviar prueba**.
