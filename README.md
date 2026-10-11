# Procedural World Generator

Gerador determinístico e independente de engine para mundos de jogos. A biblioteca produz dados puros em .NET Standard 2.1; Godot, Unity, Unreal ou uma engine própria convertem esses dados em malhas, colisores, água, vegetação e objetos.

## Sistemas reunidos

- **ProceduralMap:** hash posicional, ruído, relevo, clima, biomas, hidrologia e chunks.
- **SimulationLife:** geologia em várias escalas, rios, personalidade de biomas e cavernas exploráveis.
- **Labyrinthitis:** corredores, salas, arenas, nichos e camadas estruturais do mundo.

## Geração atual

- massas continentais, cadeias montanhosas contínuas, sopés, bacias, falhas e planaltos;
- erosão térmica e acabamento fluvial com limite de inclinação extrema;
- drenagem determinística, rios conectados, largura por vazão e planícies de inundação;
- clima com altitude, latitude, umidade e sombra de chuva;
- cavernas volumétricas conectadas, entradas espaçadas e rampas caminháveis;
- malha pronta para adaptadores de engine, com normais e níveis de detalhe;
- diagnóstico multiseed para paredes artificiais, rios interrompidos e cavernas inacessíveis.

## Perfis de terreno

`Natural`, `Alpine`, `Canyonlands`, `Karst`, `RollingPlains`, `Glacial` e `Volcanic`.

```csharp
var settings = TerrainPresetCatalog.Create(TerrainStyle.Karst, size: 129);
var world = new WorldGenerator(seed: 42, settings).Generate();
var report = TerrainDiagnostics.Analyze(world);
var mesh = TerrainMeshBuilder.Build(world.Surface, sampleStep: 1);
```

`SurfaceMap` expõe altura, declividade, temperatura, umidade, fluxo, profundidade de água, sedimento, cristas, continentalidade, receptor de drenagem, canal principal, bioma e identidade geológica. `ChunkExtractor` entrega os campos necessários para streaming em qualquer engine.

## Execução

```text
dotnet run --project samples/ProceduralWorld.Studio --urls http://127.0.0.1:5179
dotnet run --project samples/ProceduralWorld.Demo -- 2026 Alpine
dotnet run --project tests/ProceduralWorld.Tests
```

Abra `http://127.0.0.1:5179` para usar o **Procedural World Studio**. A ferramenta permite escolher o perfil geológico, a seed, a resolução, a hidrologia, a altura das montanhas, a força do relevo, a erosão, a largura dos rios, a densidade das cavernas e a escala dos biomas. A pré-visualização possui camadas de relevo, biomas, água e declividade, diagnóstico automático e exportação da configuração em JSON.

O Studio é uma ferramenta de autoria separada dos jogos. Ele consome `ProceduralWorld.Core`, mas nenhuma interface do Studio precisa ser incorporada ao HUD do jogo final.

### Criar um projeto em uma engine

O botão **Criar na Engine** procura instalações locais do Godot, Unity e Unreal. O assistente permite escolher a pasta de destino, visualizar antecipadamente todos os arquivos e autorizar explicitamente a criação e a abertura da engine.

- **Godot:** projeto executável com terreno, cores de bioma, colisão, câmera e iluminação.
- **Unity:** estrutura de projeto com `Terrain`, câmera e iluminação criados a partir do mundo exportado.
- **Unreal Engine:** `.uproject`, heightmap RAW16, mapa de biomas e instruções de importação.
- **Genérico:** dados JSON, RAW16 e RAW8 que podem ser consumidos por qualquer runtime.

O núcleo nunca executa uma engine. Essa responsabilidade fica no Studio e ocorre apenas depois da confirmação do usuário. Caminhos de sistema e raízes de disco são rejeitados, e pastas não vazias exigem autorização adicional para substituição.

Use `UniversalWorldGenerator` para receber terreno, geologia, rios, cavernas e labirinto em um único pacote. `IWorldAdapter` e os planos de streaming conectam os dados ao runtime escolhido. Veja `MIGRATION.md` para a origem de cada sistema.
