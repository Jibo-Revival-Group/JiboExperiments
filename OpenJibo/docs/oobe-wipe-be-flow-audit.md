# Jibo OOBE, Wipe, BE Skill y Cierre de Flujo

Fecha: 2026-06-12

Este documento resume la auditoria del dump `JiboOS-10.0.18`, la app React Native
`Jibo_APP` y el servidor `.NET` de `OpenJibo`, con foco en cerrar el flujo
completo de wipe -> OOBE -> servidor -> modo normal.

## Fuentes revisadas

- Dump completo: `jibo_full_dump.bin`.
- Skills extraidas: `JiboOS-10.0.18/v10.0.18_skills`.
- OOBE: `JiboOS-10.0.18/v10.0.18_skills/oobe-config`.
- BE: `JiboOS-10.0.18/v10.0.18_skills/@be/be`.
- Settings/wipe: `@be/settings` dentro de `@be/be/node_modules`.
- SDK runtime: `@be/be/node_modules/jibo/lib/jibo.js`.
- Cliente cloud legacy: `@jibo/jibo-server-client`.
- App: `Jibo_APP`.
- Servidor: `OpenJibo/src/Jibo.Cloud/dotnet`.

## Dump y arranque

El dump tiene particiones rootfs A/B, recovery, services, var y skills. En modo
normal el SSM arranca `@be/be` como skill unica (`singleSkill: true`). Los
ficheros persistentes que importan para este flujo son:

- `/var/jibo/credentials.json`: `accessKeyId`, `secretAccessKey`, `region`.
- `/var/jibo/keys/symmetric-<loopId>.json`: clave simetrica STS.
- `/usr/local/etc/jibo-jetstream-service.json`: region, `api.jibo.com`,
  `api-socket.jibo.com`, and the legacy `neo-hub.jibo.com`.

Importante: en este firmware conviene mantener `region: "api"` y resolver
DNS/hosts hacia OpenJibo. Cambiar la region a `openjibo-local` hace que
servicios legacy deriven hosts tipo `openjibo-local.jibo.com`.

## Wipe

El wipe lo inicia `@be/settings`.

Rutas clave:

- `@be/settings/index.js`: `ErrorDisplay.tapActions.wipe`, `doWipe`,
  `WipeSkill`.
- `@be/settings/assets/wipe/*.json`: confirmaciones visuales.
- `jibo/lib/jibo.js`: `WipeUtil.run`.

Flujo observado:

1. Desde error/settings, tap action `wipe` abre vista/subskill de wipe.
2. El flujo confirma varias veces. Si no hay backup passphrase, muestra
   confirmaciones adicionales.
3. `WipeView.run()` intenta `jibo.systemManager.backup()`.
4. `WipeUtil.run()` lee modo actual con `systemManager.getMode()`.
5. Si estaba en `normal`, cambia a `oobe`.
6. Si hay Wi-Fi, llama a `kb.loop.suspend()` contra `POST /v1/loop/suspend`.
7. Ejecuta `systemManager.wipe()` local.
8. Fuerza subida de logs si hay Wi-Fi.
9. Borra credenciales con
   `systemManager.setCredentials({ accessKeyId:"", secretAccessKey:"", region:"" })`.
10. Borra redes con `wifi.removeAllNetworks()`.
11. Reinicia.

Conclusion: el borrado real y el cambio a OOBE son locales al robot. El servidor
debe tolerar/cubrir backup, logs y especialmente `POST /v1/loop/suspend`; despues
debe permitir un OOBE limpio.

## OOBE

Rutas clave:

- `oobe-config/behaviors/oobe/config.js`.
- `oobe-config/behaviors/oobe/cloud-init.js`.
- `oobe-config/behaviors/oobe/main.js`.
- `oobe-config/behaviors/oobe/ota-download.js`.

El QR contiene chunks con cabecera `codeId/totalCodes\nchunk`. OOBE reensambla
chunks, aplica XOR con la clave legacy y espera lineas:

1. SSID.
2. Password.
3. Opcional: `staticIP`, `netmask`, `gateway`, `dns1`, `dns2`.
4. Token OOBE.

Despues:

1. El robot borra redes anteriores.
2. Crea la red Wi-Fi con `jibo.wifi.addNetwork(...)`.
3. `cloud-init.js` crea `JSC.OOBE` con region `api` y credenciales temporales
   `openjibo-oobe/openjibo-oobe`.
4. Llama `OOBE_20161026.SetupRobot({ token, id: robotName })`.
5. Recibe `accessKeyId`, `secretAccessKey`, `serviceMode`.
6. Guarda credenciales con
   `systemManager.setCredentials({ accessKeyId, secretAccessKey, region })`.
7. Si `serviceMode`, cambia a modo `service`; si no, sigue OTA y acaba poniendo
   modo `normal`.

## BE Skill

`@be/be` carga subskills desde su `package.json`; el default es `@be/idle`,
first-contact es `@be/first-contact`, restore es `@be/restore`, y EOS es
`@be/surprises`.

Al inicializar:

1. Llama `jibo.init({ display: "face", analytics })`.
2. Conecta al service registry (`jibo.registryHost`, puerto local 8181).
3. Inicializa `NotificationsDispatcher`.
4. Indexa el robot.
5. Inicializa plugins de `BeSkill`.
6. Decide primera skill:
   - error actual -> `@be/settings`.
   - primer arranque + backup -> `@be/restore`.
   - primer arranque sin backup -> `@be/first-contact`.
   - normal -> `@be/idle`.

BE no habla con el cloud como un unico cliente propio para todo. Consume
servicios del SDK/SSM, KB, STS, jetstream, notifications y action system. Los
resultados cloud entran por `jibo.globalEvents.skillRelaunch` / jetstream y se
convierten en skill switches o payloads de skill.

## Contrato servidor que Jibo necesita

Minimo para cerrar OOBE y primer arranque normal:

- `OOBE_20161026.PrepareRobot`, `GetStatus`, `SetupRobot`, `ReconnectRobot`.
- `Notification_20150505.NewRobotToken`.
- `Loop_20160324.List` / `ListLoops`: debe devolver exactamente un loop valido
  para SSM, con owner y robot en `members`.
- `Loop_20160324.ListMembers`.
- `POST /v1/loop/suspend` para wipe legacy.
- `Key_20160201.ShouldCreate`, creacion/carga de clave simetrica STS.
- `Update_*` para no-op/update manifest.
- `Backup_*`, logs y uploads para no bloquear wipe/OTA.
- WebSockets en `api-socket.jibo.com` y el legado `neo-hub.jibo.com` para listen/proactive.

## Contraste React Native

La app ya generaba QR compatible con OOBE:

- misma clave XOR;
- mismo formato de chunks;
- mismas lineas de payload;
- token procedente de `PrepareRobot` cuando el servidor responde.

Cambios hechos:

- `Jibo_APP/App.tsx`: si `PrepareRobot` falla pero hay `serverUrl`, conserva el
  token fallback `JiboLivesSo` para que `ScreenSetup` pueda hacer polling de
  `GetStatus`.
- `Jibo_APP/src/api/jiboApi.ts`: `getLoops`, `getLoopMembers`, `inviteMember`,
  `updateMember` y `listMedia` aceptan tanto respuestas directas del servidor
  `.NET` como wrappers historicos (`{loops}`, `{members}`, `{loop}`, `{media}`).
- `Jibo_APP/__tests__/jiboApi.test.ts`: tests de contrato para respuestas
  directas.

Estado: la app puede pedir token OOBE, construir QR consumible por Jibo y leer
loops/media del servidor `.NET` despues del setup.

## Contraste servidor .NET

El servidor ya cubria la mayor parte del contrato:

- OOBE emite tokens, acepta token dinamico o fallback estatico, devuelve
  credenciales y marca `GetStatus.complete`.
- El estado en memoria/SQLite siembra loop, owner y robot member para
  satisfacer `_isLoopGood`.
- Host routing cubre `api.jibo.com`, `api-socket.jibo.com`, y el legado `neo-hub.jibo.com`.
- WebSockets aceptan tokens y mapean LISTEN/ASR/NLU a respuestas BE.
- STT local por buffered Ogg/Opus existe con whisper.cpp.

Cambios hechos:

- `JiboCloudProtocolService.cs`: `POST /v1/loop/suspend` ahora se enruta
  explicitamente a `SuspendLoop`.
- `JiboCloudProtocolServiceTests.cs`: test para ese REST legacy.
- `BufferedAudioSttPathResolver.cs`: nuevo resolver de paths para `ffmpeg`,
  `whisper-cli` y modelo.
- `LocalWhisperCppBufferedAudioSttStrategy.cs`: usa paths resueltos al
  construirse.
- `appsettings.json`: deja vacios los paths Linux hardcodeados para que el
  resolver o env vars decidan.
- `LocalWhisperCppBufferedAudioSttStrategyTests.cs`: tests para env vars, paths
  relativos y discovery macOS de defaults Linux legacy.

## Whisper en macOS

Antes el config apuntaba a:

- `/usr/bin/ffmpeg`
- `/usr/bin/whisper.cpp/build/bin/whisper-cli`
- `/usr/bin/whisper.cpp/models/ggml-base.en.bin`

Ahora el resolver:

- respeta paths explicitos relativos o absolutos existentes;
- acepta `OPENJIBO_STT_FFMPEG_PATH`, `OPENJIBO_STT_WHISPER_CLI_PATH`,
  `OPENJIBO_STT_WHISPER_MODEL_PATH`;
- tambien acepta `FFMPEG_PATH`, `WHISPER_CLI_PATH`, `WHISPER_MODEL_PATH`;
- si ve config vacia o los defaults Linux legacy, busca rutas macOS tipicas de
  Homebrew y `~/whisper.cpp`;
- mantiene fallback a comandos relativos `ffmpeg` y `whisper-cli`.

Estado en este Mac:

- `ffmpeg`: `/opt/homebrew/bin/ffmpeg`.
- `whisper-cli`: `/opt/homebrew/bin/whisper-cli` instalado con
  `brew install whisper-cpp`.
- modelo: `~/Library/Application Support/openjibo/whisper/ggml-base.en.bin`.

Homebrew no instala modelos por defecto. El resolver incluye ese path de usuario
para que el servidor lo encuentre sin variables de entorno; si se quiere usar
otra ubicacion, definir `OPENJIBO_STT_WHISPER_MODEL_PATH` sigue siendo la forma
mas clara.

Verificacion local:

- `say` genero una muestra AIFF en macOS.
- `ffmpeg` la convirtio a WAV mono 16 kHz.
- `whisper-cli` cargo el modelo local con Metal/CPU y transcribio la muestra
  como `Hello Jibbo from Mac`.
- La transcripcion sale por `stdout` y los logs/timings por `stderr`, que
  coincide con lo que parsea `LocalWhisperCppBufferedAudioSttStrategy`.

Conclusion: en este Mac, la cadena externa que usa el servidor
(`ffmpeg` -> `whisper-cli` -> parseo de `stdout`) funciona. Lo que queda
pendiente es validarlo end-to-end con audio real entrando desde Jibo por
WebSocket hasta la estrategia STT del servidor en vivo.

## Gaps y riesgos restantes

- `Loop.SuspendLoop` responde OK pero no marca `IsSuspended`; para el wipe basta,
  pero si queremos semantica cloud exacta conviene persistir ese estado.
- `Key.CreateRequest`/backup STS esta implementado de forma minima; hay que
  seguir validandolo con capturas STS reales.
- WebSocket/ASR cubre el flujo principal y muchas skills locales, pero no
  equivale todavia a todo el cloud legacy.
- Backup/log/upload son stubs funcionales para no bloquear el flujo, no una
  infraestructura de backup completa.
- Si `PrepareRobot` falla porque la app no puede alcanzar el servidor, el QR
  fallback puede servir al robot, pero el polling de la app dependera de que el
  servidor vuelva a estar accesible desde el telefono.

## Resultado actual

Con los cambios de esta auditoria, la implementacion actual cierra el camino
critico:

wipe local -> modo OOBE -> QR RN -> Wi-Fi -> OOBE SetupRobot -> credenciales ->
loop valido -> STS/update/logs basicos -> BE normal.

Queda por validar con Jibo fisico que el STS concreto de este firmware no
necesita mas campos en operaciones avanzadas de `Key_*`.

## Verificacion ejecutada

- `dotnet test OpenJibo/tests/Jibo.Cloud.Tests/Jibo.Cloud.Tests.csproj`: 595
  tests passed. Aparecieron warnings `NU1900` por lock/cache de vulnerabilidades
  NuGet, sin fallo de compilacion ni tests.
- `dotnet test OpenJibo/tests/Jibo.Cloud.Tests/Jibo.Cloud.Tests.csproj --filter
  LocalWhisperCppBufferedAudioSttStrategyTests`: 14 tests passed.
- `whisper-cli -m "$HOME/Library/Application Support/openjibo/whisper/ggml-base.en.bin"
  -f /tmp/openjibo-whisper-test.wav -l en`: transcribio una muestra generada en
  macOS como `Hello Jibbo from Mac`.
- `npm test -- --runInBand` en `Jibo_APP`: 2 suites, 10 tests passed.
- `npm run typecheck` en `Jibo_APP`: passed.

---

# Verificación a nivel de código fuente (2026-06-12, segunda pasada)

Esta sección re-verifica cada afirmación de arriba contra el código real
(dump, app y servidor) con referencias `fichero:linea`. El objetivo es que el
flujo no dependa de resúmenes previos sino de lo que el firmware realmente
ejecuta.

## 1. Wipe — confirmado contra el firmware

Cadena exacta (todo local al robot, el cloud sólo recibe llamadas best-effort):

1. `@be/settings/index.js:3471` `WipeView.run(skipBackup)`:
   - si `!skipBackup` -> `jibo.systemManager.backup()` (`:3479`) -> backup
     nativo.
   - luego `jibo.utils.WipeUtil.run(log, skipBackup)` (`:3490`).
   - al terminar -> `jibo.systemManager.reboot()` (`:3497`).
2. `jibo/lib/jibo.js:21423` `WipeUtil.run`:
   - `getMode` (`:21426`); si `normal` -> `setMode('oobe')` (`:21433`).
   - si hay Wi-Fi -> `kb.loop.suspend()` (`:21455`); tolera `LOOP_NOT_FOUND`
     (`:21458`).
   - `systemManager.wipe()` local (`:21480`).
   - `systemManager.forceLogs()` (`:21494`).
   - `setCredentials({accessKeyId:"",secretAccessKey:"",region:""})` (`:21505`).
   - `wifi.removeAllNetworks()` (`:21520`).

Contrato de servidor que toca el wipe: `Loop_20160324.SuspendLoop`
(`@jibo/jibo-server-client/apis/loop-2016-03-24.min.json:355`) y el atajo REST
`POST /v1/loop/suspend`; backup y logs. Ninguna de esas llamadas debe bloquear:
el wipe continúa aunque fallen.

## 2. OOBE — confirmado contra el firmware

- **Región y filtro OTA** vienen de `oobe-config/config.json`:
  `{ "serverRegion": "api", "otaFilter": "eau" }`. Se cargan en
  `oobe-config.js` (bundle 7) -> `b.region = y.serverRegion`,
  `b.otaFilter = y.otaFilter`. Es decir, OOBE habla con región `api`, no con
  `openjibo-local`. Coincide con la nota de `region: "api"`.
- **Decodificación QR** (`behaviors/oobe/config.js:316-356`): reensambla chunks,
  XOR con la clave `'Wow, you cracked our secret code...jibo.com/jobs.'`
  (`:328`), `split('\n')`, `pop()` = accessToken (`:331`), y el resto es
  `[ssid, password, staticIP, netmask, gateway, dns1, dns2]` (`:335-341`).
- **Setup** (`behaviors/oobe/cloud-init.js`):
  - configura `JSC` con `region: blackboard.region` y credenciales
    `openjibo-oobe/openjibo-oobe` (`:25-28`, `:56-58`).
  - `oobe.setupRobot({ token: notepad.accessToken, id: blackboard.robotName })`
    (`:62-67`).
  - guarda `{accessKeyId, secretAccessKey, region}` con
    `jibo.systemManager.setCredentials` (`:81`).
  - si `serviceMode` -> `setMode('service')` + reboot (`:91-101`); si no,
    continúa (`:88-89`).
- **Limpieza al entrar en OOBE** (`behaviors/oobe/main.js:34-89`):
  `setMode('oobe')` si no lo estaba, borra credenciales y redes Wi-Fi. Esto
  explica por qué tras un wipe el primer arranque está totalmente limpio.
- **OTA** (`behaviors/oobe/ota-download.js` + `Updater` en `oobe-config.js`
  bundle 6): `checkForUpdates(otaFilter)`; si la lista viene vacía ->
  `onStatus(1); onDone(true)` -> `otaComplete = true`. Sin updates, OOBE termina
  y `main.js:550` hace `setMode('normal')`.

## 3. BE — decisión de primera skill confirmada

`@be/be/index.js:225-264` (`selectFirstSkill`):

- `currentErrorId` presente (`jibo.errors.getCurrentErrorId`) -> `@be/settings`
  con `nlu.entities.errorId` (`:248-250`).
- `firstTime` = `!rootNode.data.hasAlreadyLaunchedFirstContact` en la KB
  (`/skills-config`, `:225`, `:242`):
  - con `hasBackupData` (`jibo.secureTransferService.hasBackupData`, `:230`) ->
    `@be/restore` (`:257-258`).
  - sin backup -> `@be/first-contact` (`firstSkill`, `:261`).
- en otro caso -> `@be/idle` (`:238`).

Implicación para el servidor: la comprobación de backup (`hasBackupData`) la
resuelve el binario STS local, no el cloud; con el servidor respondiendo
"sin backup", el primer arranque post-OOBE entra en `@be/first-contact`, que es
el camino limpio deseado.

## 4. App React Native — contraste verificado

- `Jibo_APP/src/wifiQr.ts` genera exactamente el formato que decodifica
  `config.js`:
  - misma clave XOR (`:28-29`), mismo orden de líneas
    `ssid, password, [staticIP, netmask, gateway, dns1, dns2], token`
    (`buildSetupPayload :43-51`), token al final.
  - chunking `id/totalCodes\n<cuerpo>` (`splitEncodedPayload :55-64`), que casa
    con el parser del robot (`codeId/totalCodes`).
  - El robot hace XOR del payload reensamblado; la app hace XOR y luego
    parte en chunks. Es consistente (concatenar cuerpos == payload XOR).
- `Jibo_APP/src/api/jiboApi.ts`:
  - `prepareRobot` -> `OOBE_20161026.PrepareRobot` (`:272-287`); fallback a
    `STATIC_ACCESS_TOKEN = 'JiboLivesSo'` en `App.tsx:242` si falla.
  - `getOobeStatus` -> `GetStatus` (`:289-300`).
  - parsers `unwrapArray`/`unwrapRecord` (`:97-107`) toleran respuesta directa
    del `.NET` y wrappers históricos (`{loops}`, `{members}`, `{loop}`, `{media}`).
  - cabeceras AWS: `Content-Type: application/x-amz-json-1.1` + `X-Amz-Target`
    (`:72-74`).

## 5. Servidor .NET — contraste verificado

- **OOBE** (`JiboCloudProtocolService.cs:152-209`):
  - `PrepareRobot` emite token `oobe-<hex>` y guarda `LoopId`/`AccountId`
    (`:152-167`).
  - `SetupRobot`/`ReconnectRobot` aceptan **cualquier** token, incluido
    `JiboLivesSo` (`GetOrAdd`, `:186`), marcan `Complete=true` y devuelven
    `{accessKeyId, secretAccessKey, serviceMode:false}` (`:201-206`).
  - `GetStatus` devuelve `complete` sólo si el token existe y está completo
    (`:170-176`).
- **Loop** (`:431-444`, `MapLoopRecord :1365-1378`): `List/ListLoops` devuelve
  el array de loops con `owner`, `robot`, `members`. La siembra
  (`InMemoryCloudStateStore.cs`): `EnsureDefaultTopology` (`:947`) crea el loop
  con `OwnerAccountId`/`RobotId` y un miembro owner; `EnsureRobotLoopMember`
  (`:918-939`) añade el miembro `type:"robot"` con `AccountId == RobotId`.
  Esto satisface `_isLoopGood`: un solo loop, `members` no vacío, `owner` y
  `robot` presentes en `members[].accountId`.
- **Suspend** (`:137-139`, `:562-564`): `POST /v1/loop/suspend` se enruta a
  `SuspendLoop`, que responde `{result:"ok"}` (no persiste `IsSuspended`).
  Suficiente para el wipe.
- **Key/STS** (`:728-785`): `ShouldCreate`, `CreateSymmetricKey`/`LoadSymmetricKey`,
  `CreateRequest`, `GetRequest`, `Share`. Resto -> no-op `{ok:true}`.
- **Update/OTA** (`:864-915`): `ListUpdates`/`ListUpdatesFrom` devuelven el
  store (vacío por defecto -> OOBE ve "sin updates" -> `otaComplete`);
  `GetUpdateFrom` devuelve un no-op (`BuildNoopUpdate`) cuando no hay nada.
- **WebSockets** (`Program.cs:18-30`): `UseWebSockets` + coordinator para
  listen/proactive/hub.

## Discrepancia encontrada (inofensiva)

`Jibo_APP/src/api/jiboApi.ts:281` envía `loopId: 'loop-openjibo-default'` en
`PrepareRobot`, mientras que el loop canónico del servidor es
`openjibo-default-loop` (p.ej. `LoopRecord.cs:5`). No rompe el flujo: el
servidor guarda ese `LoopId` en el `OobeTokenState` pero nunca lo usa en
`SetupRobot` (las credenciales se derivan de `AccountId`/cuenta,
`:197-206`). Conviene alinear el literal para evitar confusión futura, pero no
es bloqueante.

## Conclusión de la segunda pasada

El camino crítico está cerrado y verificado contra el código real:

```text
wipe (local: oobe + suspend + wipe + clear creds/wifi + reboot)
  -> arranque en modo oobe (limpieza de creds/redes)
  -> QR de la app RN (XOR + chunks idénticos al decoder)
  -> Wi-Fi + SetupRobot(token) -> credenciales
  -> setMode normal (sin updates)
  -> @be/be: loop válido (_isLoopGood) + STS + sin backup -> @be/first-contact
```

Gaps que siguen siendo "validar con hardware", no bloqueantes para el flujo:
`IsSuspended` no persistido, STS `Key_*` avanzado mínimo, backup/log como stubs,
y alinear el literal `loopId` de la app.
