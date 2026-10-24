# CMS 2027 – Shared Inventory

Mod para **Car Mechanic Simulator 2027 (Multiplayer Demo)** que faz todos os jogadores de uma partida multiplayer usarem **o mesmo inventário**.

> Mod for Car Mechanic Simulator 2027 that makes every player in a multiplayer session share one inventory (the host's).

## Instalação (jogador)

1. Baixe `CMS2027-SharedInventory-Installer.exe` em [Releases](../../releases).
2. Feche o jogo, abra o instalador e clique em **Instalar** (ele acha o jogo pela Steam sozinho).
3. Abra o jogo. **Na primeira vez ele demora alguns minutos para abrir** — é normal.

**Todos os jogadores da partida precisam instalar.**

Para remover: abra o instalador e clique em **Desinstalar**.

> O Windows SmartScreen pode avisar que o app não é reconhecido (o exe não é assinado). Clique em "Mais informações" → "Executar assim mesmo".

## Como funciona

O jogo dá a cada jogador um inventário próprio: o cliente manda cada mudança (adicionar / remover / atualizar pilha) ao host, que guarda uma cópia por jogador.

Com o mod:

- O inventário do **host** é o inventário compartilhado.
- Quando chega uma mudança de um cliente, o host copia a lista daquele cliente para o inventário compartilhado.
- Sempre que o inventário compartilhado muda, o host reenvia ele para todos usando os próprios pacotes de "full sync" do jogo.
- O cliente aplica essa resincronização na lista que já existe, para a interface atualizar e nada voltar para o host.
- Quem entra na partida recebe o inventário compartilhado em vez do seu.

### Limitações

- Se duas pessoas mexerem no inventário no mesmo instante, vale a última mudança (pode sumir um item nessa colisão).
- O save do host grava o inventário compartilhado como o inventário de cada jogador. Se remover o mod, cada um fica com uma cópia de tudo.
- Feito para a demo. Atualizações do jogo podem quebrar o mod.

## Compilando

Requisitos: .NET SDK 8, o jogo instalado com BepInEx 6 IL2CPP e aberto uma vez (para gerar `BepInEx\interop`).

```powershell
./build.ps1 -GameDir "D:\Steam\steamapps\common\Car Mechanic Simulator 2027 Demo"
```

Saída em `dist/`: o instalador (com BepInEx e o mod embutidos) e `SharedInventory.dll`.

Estrutura:

- `src/SharedInventory` – plugin BepInEx (Harmony).
- `src/Installer` – instalador WinForms (.NET Framework 4.8, já vem no Windows).

## Créditos

- [BepInEx](https://github.com/BepInEx/BepInEx) (LGPL-2.1), embutido no instalador.
- [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop), [HarmonyX](https://github.com/BepInEx/HarmonyX).

Não afiliado à Red Dot Games.
