# Bug : UI Blur — image grise au lieu du blur

## Contexte

Unity 6000.2.6f2, URP 17, jeu de cartes avec beaucoup de World Space canvases en scène.

Objectif : effet "verre dépoli" — une `Image` sur un Canvas **Screen Space - Camera** doit afficher avec blur gaussien tout ce qui est derrière elle (scène 3D + world-space canvases).

## Approche implémentée

1. `BackgroundCaptureFeature` (ScriptableRendererFeature) copie le color buffer après `AfterRenderingTransparents` dans une texture globale `_BackgroundBlurSource`
2. Shader URP `Custom/UI_Blur` sur l'Image sample cette texture avec un kernel gaussien 3×3

Fichiers concernés :
- `Assets/Scripts/Rendering/BackgroundCaptureFeature.cs`
- `Assets/Scripts/Rendering/Game.Rendering.asmdef`
- `Assets/Shaders/UI_Blur.shader`

## Setup confirmé

- Feature compilée sans erreur
- Feature ajoutée au `PC_Renderer` asset (`Assets/Settings/PC_Renderer.asset`)
- Canvas du blur : **Screen Space - Camera**
- `m_RequireOpaqueTexture: 1` dans `PC_RPAsset`
- Renderer en mode **Deferred** (`m_RenderingMode: 2`)
- `m_IntermediateTextureMode: 0`

## Symptôme

Image affiche gris uniforme. La texture `_BackgroundBlurSource` semble ne jamais être populée OU le shader ne la sample pas correctement.

## Pistes à investiguer

- `Blitter.BlitCameraTexture` ne copie pas le bon buffer en mode Deferred
- En mode Deferred + `IntermediateTextureMode: 0`, le color target peut être le backbuffer natif — pas blit-able
- Screen Space - Camera canvas render dans un contexte séparé, texture globale peut ne pas persister
- Flipping UV vertical (convention DX vs OpenGL) — le contenu est là mais inversé/hors écran
- `cameraColorTargetHandle` null ou invalide au moment du Execute

## Demande

Diagnose la cause racine et fournis :
1. `BackgroundCaptureFeature.cs` corrigé
2. `UI_Blur.shader` corrigé si nécessaire

Doit compiler en Unity 6000.2.6f2 / URP 17 sans warning bloquant.
