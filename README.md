# SERENA · App del estudiante

Aplicación Unity para las tablets Android de los estudiantes en SERENA, un sistema multimedia para el aprendizaje social, emocional y ético en el aula. Con ella el estudiante entra a la sesión del docente, ve la introducción, elige una emoción y una decisión, ve la consecuencia de su elección, vota y responde las preguntas de reflexión.

El servidor, la web del docente y el lanzador están en **[SERENA-AppDocente](https://github.com/JCobs23/SERENA-AppDocente)**. Su README explica el proyecto completo, la arquitectura y cómo se hizo.

## Requisitos

- Unity **6000.6.3f1** con el módulo **Android Build Support**.
- Tablets Android (probado con Samsung Galaxy Tab A9+).
- El servidor de SERENA ejecutándose en el portátil del docente, en la misma red wifi.

## Estructura

```text
Proy - AppEstudiante/
├─ Assets/
│  ├─ Scenes/
│  │  ├─ 1_IngresoCodigo ...... código de la sesión y dirección del servidor
│  │  ├─ 2_SampleScene ........ nombre, sala de espera y botón Ready?
│  │  └─ 3_Narrativa .......... introducción, cinco etapas, votación y reflexión
│  ├─ Scripts/
│  │  ├─ SerenaServerConfig ............ URL del servidor (guardada en PlayerPrefs)
│  │  ├─ SessionCodeEntryController .... validación del código y botón «Server»
│  │  ├─ StudentNameController ......... registro del nombre
│  │  ├─ StudentReadyButtonController .. estado Ready? / I'm ready
│  │  ├─ StudentRosterController ....... lista de compañeros (cada 1 s)
│  │  ├─ NarrativeStartListener ........ estado de la sesión (0,5 s), latido (3 s) y reconexión
│  │  ├─ NarrativeStageView ............ video, emoción, decisión, consecuencia, voto y reflexión
│  │  └─ StudentProgressView ........... línea de tiempo, «Etapa N de 5» y mensajes de avance
│  ├─ StreamingAssets/ ...... SerenaIntro.mp4, SerenaNarrativa.mp4 y un Decision_*.mp4 por decisión
│  └─ Resources/Emociones/ .. ilustraciones de las cinco emociones
└─ Documentation/
   ├─ Guia-preparar-aula.md ........... preparar la red del aula en una página
   └─ HU12.1-Configuracion-de-red.md .. parámetros de red (router, IP y puertos)
```

## Cómo se comunica con el servidor

La app usa `UnityWebRequest` con REST/JSON contra `http://<IP del portátil>:5080`:

| Acción | Endpoint |
| --- | --- |
| Validar el código | `GET /api/sessions/{code}` |
| Registrar el nombre (o volver con el mismo ID) | `POST /api/sessions/{code}/students` |
| Listo / latido / salida | `POST .../students/{id}/ready`, `/heartbeat`, `/leave` |
| Introducción terminada | `POST .../students/{id}/intro-complete` |
| Emoción, decisión, voto y reflexión | `POST .../students/{id}/emotion`, `/decision`, `/vote`, `/reflection` |

La app consulta el estado cada 0,5 s. Cuando el docente cambia de etapa, pausa el video o vuelve al catálogo, la tablet lo aplica en la siguiente consulta. Si se pierde la conexión, reintenta sola y vuelve con el mismo identificador. El contrato completo está en `CONTRATO-ESTADO-SESION.md` del repositorio del docente.

## Ejecutar y compilar

1. Abre la carpeta `Proy - AppEstudiante` con Unity Hub (versión 6000.6.3f1).
2. Para probar en el Editor, abre `Assets/Scenes/1_IngresoCodigo` y pulsa Play con el servidor en ejecución.
3. Con el botón **Server** de la pantalla de código, escribe la dirección del portátil del docente, por ejemplo `http://192.168.1.20:5080`. En una tablet no sirve `localhost`, porque apunta a la propia tablet; usa la IPv4 que muestra `ipconfig` en el portátil. El valor queda guardado en la tablet.
4. Para generar la APK, ve a **File → Build Profiles**, elige **Android**, revisa que la lista de escenas empiece por `1_IngresoCodigo`, `2_SampleScene` y `3_Narrativa`, y pulsa **Build**. Luego instálala en cada tablet.

## Videos

Los videos se leen desde `StreamingAssets`, así que funcionan sin internet:

| Archivo | Uso |
| --- | --- |
| `SerenaIntro.mp4` | Introducción que cada tablet reproduce al iniciar |
| `SerenaNarrativa.mp4` | Fotograma de fondo de las etapas 2 y 3 |
| `Decision_DialogarDirectamente.mp4` | Consecuencia de «Talk directly» |
| `Decision_EvitarInvolucrarse.mp4` | Consecuencia de «Avoid getting involved» |
| `Decision_DifundirRumor.mp4` | Consecuencia de «Spread the rumor» |
| `Decision_CambiarTema.mp4` | Consecuencia de «Change the subject» |

Para reemplazar un video, usa el mismo nombre de archivo, en formato MP4 con H.264, y vuelve a generar la APK.
