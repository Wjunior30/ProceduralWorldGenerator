# Procedural World Generator

Gerador determinístico e independente de engine para mundos de jogos. A biblioteca produz dados puros em .NET Standard 2.1; Godot, Unity, Unreal ou uma engine própria convertem esses dados em malhas, colisores, água, vegetação e objetos.

## Sistemas reunidos

- **ProceduralMap:** hash posicional, ruído, relevo, clima, biomas, hidrologia e chunks.
- **SimulationLife:** geologia em várias escalas, rios, personalidade de biomas e cavernas exploráveis.
- **Labyrinthitis:** corredores, salas, arenas, nichos e camadas estruturais do mundo.

## Geração atual

- geração determinística de massas continentais, cadeias montanhosas, sopés, bacias, falhas e planaltos;
- erosão térmica, acabamento fluvial e proteções contra degraus extremos;
- drenagem por acumulação de fluxo, canais com largura variável e modos de rios e lagos;
- clima calculado com altitude, latitude, umidade e sombra de chuva;
- cavernas volumétricas, entradas de superfície e uma representação implícita para adaptadores;
- malhas com normais e níveis de detalhe para integração com engines;
- backtest multiseed e diagnósticos de terreno, rios, biomas e cavernas.

Esses sistemas formam uma base funcional e independente de engine. A geração v4 resolve a geologia antes do clima, conserva massa durante a erosão, recalcula a drenagem sobre o terreno final, controla as saídas de borda e mantém uma única rede subterrânea navegável. O realismo artístico continua evolutivo, mas os bloqueadores estruturais abaixo possuem validação automática multiseed e multirresolução.

## Estado da implementação v4

- **P0 concluído:** geologia estrutural, transporte conservativo, hidrologia final monotônica, cavernas voxel/implícitas derivadas da mesma rede, aberturas de superfície e saídas controladas.
- **P1 concluído como modelo procedural:** perfis geomorfológicos, classificação de formas de terreno, vento 2D configurável, hierarquia fluvial, planícies de inundação e diversidade de biomas.
- **P2 concluído:** geografia estável entre resoluções, saias e malhas de transição para LOD, mapas diagnósticos, imagens de regressão e backtest em múltiplas resoluções.
- Os critérios permanecem documentados abaixo como contrato de regressão, não como lista de tarefas pendentes.

## Prioridades geológicas

### P0 — Correção estrutural

Estes requisitos são o contrato estrutural coberto pela suíte de testes e pelo backtest.

1. **Gerar a geologia antes do clima e dos biomas.** Criar províncias tectônicas, litologia, dureza, estratos, falhas e resistência à erosão. O relevo deve nascer dessa estrutura; solo e bioma devem ser consequências dela.
2. **Substituir o limitador rígido de inclinação.** Usar transporte conservativo de material, talude, deposição e erosão hidráulica. O algoritmo não deve apenas rebaixar pontos altos até um limite fixo.
3. **Resolver a hidrologia sobre o terreno final.** Depois de escavar canais ou alterar encostas, recalcular receptores, vazão e superfície da água. Rios precisam seguir um perfil sempre descendente até uma saída, lago ou sumidouro válido.
4. **Unificar as cavernas.** A malha voxel e o sistema implícito devem representar a mesma rede. Entradas precisam abrir o terreno visível, possuir folga para o jogador e alcançar galerias navegáveis.
5. **Corrigir as condições de borda.** Criar bacias e saídas fluviais controladas em vez de transformar toda célula periférica em uma saída independente.

Critérios mínimos para concluir P0:

- menos de 1% das arestas presas exatamente ao limite de inclinação;
- nenhum receptor de drenagem subindo no terreno final;
- nenhum trecho de rio com superfície hidráulica ascendente;
- pelo menos 90% do volume jogável das cavernas conectado a uma entrada;
- entradas validadas por inclinação, altura livre e percurso contínuo até a superfície;
- conservação de massa na erosão, dentro de uma tolerância documentada.

### P1 — Personalidade geomorfológica

1. **Natural:** alternar planícies, colinas, maciços, vales e bacias sem uma inclinação dominante.
2. **Alpine:** cadeias conectadas, vales glaciais em U, circos, arestas, colos e depósitos de talude.
3. **Canyonlands:** estratos resistentes, mesas, escarpas, cânions tributários e leques aluviais.
4. **Karst:** calcário, dolinas, vales secos, rios sumidouros, ressurgências e cavernas condicionadas pela rocha.
5. **Rolling Plains:** grandes áreas navegáveis, colinas suaves, meandros, várzeas e terraços fluviais.
6. **Glacial:** vales em U, fiordes ou lagos glaciais, morainas, rocha polida e drenagem reorganizada.
7. **Volcanic:** edifícios vulcânicos, caldeiras, derrames, diques, campos de lava e tubos de lava.

Também fazem parte de P1:

- remover a quantização global de planaltos e restringir terraços às formações que realmente os utilizam;
- usar vento bidimensional configurável na simulação de umidade;
- criar confluências, ordem de cursos d'água, meandros, várzeas e deposição aluvial;
- impedir que um único bioma ou uma geologia incompatível domine o mapa sem justificativa do perfil.

### P2 — Robustez, malhas e validação

- preservar a mesma geografia ao alterar somente a resolução da malha;
- costurar bordas entre chunks e níveis de detalhe, com teste automático contra rachaduras;
- adicionar mapas de curvatura, saturação de inclinação, bacias, ordem fluvial e componentes subterrâneos aos diagnósticos;
- criar renderizações de regressão para seeds de referência de cada perfil;
- validar distribuição de áreas planas e íngremes por perfil, em vez de usar um único limite para todos;
- executar backtests em várias resoluções e rejeitar seeds com rios ascendentes, geologia incoerente ou cavernas isoladas.

## Resultado do backtest e limitações conhecidas

O backtest ampliado de outubro de 2026 gerou **700 mundos** (sete perfis, 50 seeds e resoluções 65 × 65 e 129 × 129). Todos concluíram a geração; rios ficaram hidraulicamente válidos e as cavernas alcançáveis pelas entradas nos agregados. A comparação geomorfológica obteve 100/100 em todos os casos de `Karst` e `Volcanic`, além de todos os casos de `Alpine` em 65 × 65.

Ainda há seeds extremas que devem ser rejeitadas pelos diagnósticos ou refinadas futuramente:

- o pior caso estrutural foi `Glacial`, seed `7924047624999685062`, com nota 79,6, domínio de bioma e dois componentes subterrâneos desconectados;
- 28 de 100 mapas `Natural` não equilibraram áreas planas e encostas, e dez apresentaram baixa diversidade de formas;
- 12 de 100 mapas `Glacial` ficaram fora do arquétipo, principalmente por relevo insuficiente ou falta de fundos largos de vale;
- um caso `Alpine` em 129 × 129 teve relevo relativo insuficiente;
- dois casos `RollingPlains` em 65 × 65 tiveram relevo excessivo ou área plana insuficiente;
- dois casos `Canyonlands` em 65 × 65 produziram cânions pouco encaixados;
- casos estruturais raros ainda apresentam domínio excessivo de um bioma, ápices isolados, degraus altos ou componentes subterrâneos desconectados.

Os relatórios completos ficam em `artifacts/backtest/summary.md` e `artifacts/backtest/real-geology-summary.md`. A comparação é qualitativa, baseada em formas e distribuições; a próxima etapa científica é confrontar alturas e drenagem com DEMs reais reamostrados na mesma escala.

## Perfis de terreno

`Natural`, `Alpine`, `Canyonlands`, `Karst`, `RollingPlains`, `Glacial` e `Volcanic`.

```csharp
var settings = TerrainPresetCatalog.Create(TerrainStyle.Karst, size: 129);
var world = new WorldGenerator(seed: 42, settings).Generate();
var report = TerrainDiagnostics.Analyze(world);
var mesh = TerrainMeshBuilder.Build(world.Surface, sampleStep: 1);
```

`SurfaceMap` expõe altura, declividade, curvatura, saturação de inclinação, temperatura, umidade, fluxo, ordem fluvial, bacia, profundidade de água, sedimento, cristas, continentalidade, receptor de drenagem, canal principal, bioma, forma de terreno, abertura de caverna e identidade geológica. `ChunkExtractor` entrega os campos necessários para streaming em qualquer engine.

A etapa final de geologia impõe limites reais de inclinação, remove ápices isolados sem achatar maciços largos e regulariza o perfil hidráulico dos rios. `TerrainDiagnostics` informa tanto o maior degrau entre células quanto o maior ápice local, permitindo rejeitar seeds com agulhas, paredes ou lâminas de água antes da exportação.

## Execução

```text
dotnet run --project samples/ProceduralWorld.Studio --urls http://127.0.0.1:5179
dotnet run --project samples/ProceduralWorld.Demo -- 2026 Alpine
dotnet run --project samples/ProceduralWorld.Backtest -- 65,129 artifacts/backtest
dotnet run --project tests/ProceduralWorld.Tests
```

Abra `http://127.0.0.1:5179` para usar o **Procedural World Studio**. A ferramenta permite escolher o perfil geológico, a seed, a resolução, a hidrologia, a altura das montanhas, a força do relevo, a erosão, a largura dos rios, a densidade das cavernas e a escala dos biomas. A pré-visualização mantém os modos 2D ortográfico e 3D em miniatura, com rotação e zoom, além das camadas de relevo, biomas, água e declividade, diagnóstico automático e exportação da configuração em JSON.

O backtest grava JSON, CSV, resumo Markdown e renderizações PPM de regressão para a seed de referência de cada perfil. Sem argumento de resolução, ele valida 65 × 65 e 129 × 129.

Para ampliar a amostra e executar também a comparação com arquétipos geológicos reais:

```text
dotnet run --project samples/ProceduralWorld.Backtest -- 65,129 artifacts/backtest 50
```

O terceiro argumento define a quantidade de seeds. `real-geology-summary.md` compara relevo relativo, distribuição de declives, drenagem e formas diagnósticas com arquétipos documentados pelo USGS. Essa análise é geomorfológica e não substitui uma comparação célula a célula com um modelo digital de elevação real.

O Studio oferece cinco aparências intercambiáveis sem regenerar o mundo: **mapa técnico**, **estilizado**, **realista PBR**, **satélite** e **miniatura/diorama**. Os níveis baixo, médio e alto ajustam suavização, densidade de detalhes, vegetação e custo do WebGL. A mistura visual considera altitude, declive, bioma e água, funciona no 2D e no 3D e é incluída na configuração JSON exportada.

O Studio é uma ferramenta de autoria separada dos jogos. Ele consome `ProceduralWorld.Core`, mas nenhuma interface do Studio precisa ser incorporada ao HUD do jogo final.

### Criar um projeto em uma engine

O botão **Criar na Engine** procura instalações locais do Godot, Unity e Unreal. O assistente permite escolher a pasta de destino, visualizar antecipadamente todos os arquivos e autorizar explicitamente a criação e a abertura da engine.

- **Godot:** projeto executável com terreno, cores de bioma, colisão, câmera e iluminação.
- **Unity:** estrutura de projeto com `Terrain`, câmera e iluminação criados a partir do mundo exportado.
- **Unreal Engine:** `.uproject`, heightmap RAW16, mapa de biomas e instruções de importação.
- **Genérico:** dados JSON, RAW16 e RAW8 que podem ser consumidos por qualquer runtime.

O núcleo nunca executa uma engine. Essa responsabilidade fica no Studio e ocorre apenas depois da confirmação do usuário. Caminhos de sistema e raízes de disco são rejeitados, e pastas não vazias exigem autorização adicional para substituição.

Use `UniversalWorldGenerator` para receber terreno, geologia, rios, cavernas e labirinto em um único pacote. `IWorldAdapter` e os planos de streaming conectam os dados ao runtime escolhido. Veja `MIGRATION.md` para a origem de cada sistema.
