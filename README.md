# Procedural World Generator

Gerador deterministico e independente de engine para mundos de jogos.

## Origem dos sistemas

- **ProceduralMap:** hash posicional, ruido, relevo, clima, biomas, hidrologia e chunks.
- **SimulationLife:** geologia em varias escalas, rios, personalidade de biomas e cavernas exploraveis.
- **Labyrinthitis:** corredores, salas, arenas, nichos e camadas estruturais do mundo.

O projeto gera somente dados. Unity, Godot, Unreal ou uma engine propria transformam esses dados em malhas, colisores, agua, vegetacao e objetos.

## Saidas

- `SurfaceMap`: altura, declividade, temperatura, umidade, fluxo, agua e bioma.
- `CaveVolume`: volume subterraneo real com tuneis conectados e entradas.
- `LabyrinthPlan`: salas e corredores navegaveis.
- `GeneratedWorld`: pacote completo produzido por uma seed.

## Exemplo

```csharp
var world = new WorldGenerator(42, new WorldSettings { Size = 129 }).Generate();
Console.WriteLine(world.Fingerprint().ToString("X16"));
```

