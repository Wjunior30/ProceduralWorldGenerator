# Adaptador Unity

A pasta `template` contém os scripts e o material usados pelo SimulationLife. O Studio incorpora esses arquivos e exporta um projeto Unity 6000.6.3f1 com a DLL atual do núcleo, configuração, dados e controles.

## Fluxo

1. No Studio, use **Criar na Unity**, escolha o destino e revise os arquivos.
2. Abra o projeto: a primeira importação constrói `Assets/Scenes/FrameworkWorld.unity`.
3. Inicie Play e clique em Game. WASD move, Shift corre, Espaço salta, mouse gira, roda ajusta distância, Esc libera e R retorna.

`FrameworkPreviewBuilder` constrói Terrain refinado e o rio a partir de `RiverSurfaceField`. `ThirdPersonExplorer` consulta esse mesmo campo para travessia, flutuação e correnteza. `ThirdPersonOrbitCamera` trata órbita, cursor e colisão. O menu Framework regenera usando a configuração exportada, incluindo seed e seleção de biomas.

O JSON preserva a geografia exportada. Ao regenerar entre .NET e Unity podem ocorrer pequenas diferenças numéricas; reutilize o JSON para repetir exatamente o mesmo mundo. Lagos visuais, cavernas jogáveis e respingos de contato ainda não fazem parte desta apresentação.

## Validação

A suíte `tests/ProceduralWorld.RiverTests` testa exportações com rios, sem água e com dados de lagos. Dentro da Unity, `FrameworkPreviewLauncher.ValidateAndConfigure` verifica a estrutura. Criar `Artifacts/install-third-person.request` e abrir a cena ativa as verificações de movimentação, salto, natação e câmera do mapa de referência; os resultados ficam em `Artifacts/third-person-validation.txt`.
