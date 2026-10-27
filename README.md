# ♟️ Xadrez Desktop C# — Lucas Chess & Classic Study Suite

Aplicação desktop de xadrez para Windows desenvolvida em **C# (.NET 8 / Windows Forms)** com interface clássica retrô inspirada no *Lucas Chess*. O projeto combina um motor completo de regras oficiais da FIDE, integração com o **Stockfish (via protocolo UCI)**, **Engine Tática/Posicional Interna** de fallback, treinador (*Coach*) em português, análise de precisão pós-lance, relógio digital (*Fischer* e *Bronstein*), biblioteca de aberturas e 15 desafios táticos (*Puzzles*).

---

## ✨ Funcionalidades Principais

### 1. Regras e Mecânicas Oficiais (FIDE)
- **Movimentação e Validação Completa:** Geração estrita de lances legais, impedindo qualquer movimento que deixe ou coloque o próprio Rei em xeque.
- **Lances Especiais:**
  - **Roque Pequeno (`O-O`) e Grande (`O-O-O`):** Valida se o Rei e a Torre não se moveram, se as casas intermediárias estão livres e se o Rei não está em xeque nem passa por casas atacadas.
  - **Captura *En Passant*:** Detecção e execução automática no meio-lance imediatamente posterior ao avanço duplo do peão adversário.
  - **Promoção de Peão Interativa:** Janela modal (`PromotionDialog`) permitindo escolher entre **Rainha (`Q`)**, **Torre (`R`)**, **Bispo (`B`)** ou **Cavalo (`N`)**.
- **Detecção Automática de Fim de Partida:**
  - Xeque-mate (`1-0` ou `0-1`).
  - Empate por Rei Afogado (*Stalemate*).
  - Empate pela **Regra dos 50 Movimentos** (100 meio-lances sem captura ou movimento de peão).
  - Empate por **Repetição Tripla** (mesma posição repetida 3 vezes com o mesmo lado a jogar e mesmos direitos de roque/*en passant*).
  - Empate por **Insuficiência de Material** (`Rei vs Rei`, `Rei + Bispo vs Rei`, `Rei + Cavalo vs Rei` e `Rei + Bispo vs Rei + Bispo` em casas da mesma cor).
- **Notação Algébrica Padrão (SAN):** Suporte completo a desambiguação de peças (`Nbd2`, `Rae1`, `R1e3`), capturas (`x`), xeques (`+`) e xeque-mate (`#`).

### 2. Inteligência Artificial Híbrida (Stockfish + Engine Interna)
- **Integração Nativa com Stockfish (UCI):** Conecta-se automaticamente ao executável `stockfish.exe` em processo separado e *thread-safe*, utilizando `UCI_LimitStrength`, `UCI_Elo` e `Skill Level` para calibrar a força de jogo.
- **Engine Tática e Posicional Interna (Fallback Automático):** Caso o `stockfish.exe` não esteja presente na pasta, o jogo aciona transparentemente um motor próprio com busca **Minimax Alpha-Beta**, **Busca de Quiescência** (evitando o efeito horizonte em capturas), **Tabelas Posicionais (*Piece-Square Tables*)** para centro/desenvolvimento/segurança do Rei e penalidade anti-repetição.
- **4 Níveis de Dificuldade:**
  - **Fácil (Iniciante):** Joga de forma humana, escolhendo lances candidatos secundários para permitir aprendizado.
  - **Médio (Clube):** Força intermediária (~1500 Elo).
  - **Difícil (Forte):** Cálculo tático avançado (~1900 Elo).
  - **Mestre (Máximo):** Força total do motor (Profundidade 15 / Nível 20).

### 3. Ferramentas de Estudo, Coach e Análise Pós-Lance
- **Classificação Automática de Lances:** A cada jogada sua (sempre avaliada com o motor em força máxima, independentemente do nível do bot), o sistema compara a avaliação antes e depois do lance e classifica com cores na tabela:
  - `!!` **Brilhante!** — Lance tático/sacrifício decisivo.
  - `!` **Melhor Lance** — O lance número 1 recomendado pela engine.
  - `📖` **Teórico (Livro)** — Lance que segue a teoria oficial de aberturas.
  - **Lance Bom** — Mantém o equilíbrio ou vantagem com perda mínima.
  - `?!` **Imprecisão** — Perda moderada de vantagem posicional.
  - `?` **Erro** — Perda significativa de avaliação/material.
  - `??` **Capivarada (*Blunder*)** — Erro grave que compromete a partida.
- **Cálculo de Precisão (`0%` a `100%`):** Baseado na curva de probabilidade de vitória (*Win%*), nos moldes do Lichess e Chess.com.
- **Coach em Português:** Explica na barra de status o motivo tático/posicional da avaliação (ex.: *"controle ativo do centro"*, *"roque pequeno (Rei em segurança)"*, *"perdeu 1.40 peões em relação a e2-e4"*).
- **Relatório de Precisão da Partida:** Resumo estatístico completo ao final do jogo (ou via menu `Search & Estudo -> Relatório de Precisão da Partida`), destacando a precisão média das Brancas, contagem de cada tipo de lance e o **momento crítico** da partida com a recomendação da IA.
- **Ajuda de Lance (*Help to Move*):** Desenha uma **seta verde** no tabuleiro indicando a melhor jogada disponível e exibe os principais lances candidatos.

### 4. Quebra-Cabeças (*Puzzles*) e Biblioteca de Aberturas
- **15 Puzzles Táticos Integrados:** Exercícios clássicos prontos para jogar no menu `Puzzles Táticos` (Mate Pastor, Mate do Corredor, Garfo Real, Espeto de Bispo, Mate de Legal, Mate Sufocado de Philidor, Mate de Anastásia, Mate Árabe, Mate de Boden, Mate das Ombreiras, Cravada Absoluta, Garfo de Peão, Mate da Ópera de Morphy, Presente Grego e Coroação com Mate).
- **Detecção de Aberturas em Tempo Real (*Opening Book*):** Identifica automaticamente na barra superior aberturas como *Ruy Lopez*, *Italiana (Giuoco Piano)*, *Defesa dos Dois Cavalos*, *Escocesa*, *Gambito do Rei*, *Defesa Siciliana (Najdorf)*, *Francesa*, *Caro-Kann*, *Escandinava*, *Sistema Londres*, *Gambito da Dama*, *Índia do Rei*, *Nimzo-Índia*, *Inglesa* e *Réti*.

### 5. Cronometragem Digital e Controles Avançados de Tabuleiro
- **Relógio Digital de Alta Precisão (`Stopwatch`):**
  - **Fischer:** `5+3` (5 min + 3s de acréscimo por lance), `3+2` e `10+0`.
  - **Bronstein:** `5+3` (devolve apenas o tempo efetivamente gasto no turno, até o limite de 3s).
  - **Sem Relógio:** Modo livre para estudo.
- **Interação Avançada com o Mouse:**
  - **Quadrados Laranjas:** Destacam todas as casas legais da peça selecionada.
  - **Premove (Jogada Automática):** Clique em uma peça sua e na casa de destino enquanto o bot está pensando; as casas ficam **azuis** e o lance é executado automaticamente assim que o bot jogar.
  - **Botão Direito (Setas e Marcações):** Clique com o botão direito em uma casa para demarcá-la em **vermelho/coral**, ou segure e arraste com o botão direito entre duas casas para desenhar **setas de rota**. Um clique com o botão esquerdo limpa todas as anotações.
- **Temas Customizáveis:** Alterne instantaneamente entre **Madeira**, **Preto e Branco** e **Branco e Verde**.
- **Suporte Completo a FEN e PGN:** Copie ou carregue posições **FEN** (com validador estrito de legalidade) e salve/copie o histórico completo da partida em formato **PGN** padrão.

---

## 📂 Estrutura de Pastas e Arquivos

```text
xadrez/
├── .github/
│   └── workflows/
│       └── build.yml          # Pipeline CI/CD que compila o .exe e baixa o Stockfish no GitHub Actions
├── client/
│   └── assets/                # Sprites .png das peças (variantes 1 e 2) e texturas do tabuleiro
├── Program.cs                 # Ponto de entrada (Main), DoubleBufferedPanel e PromotionDialog
├── MainForm.cs                # Interface gráfica clássica, eventos de mouse, relógio e fluxo de jogo
├── ChessRules.cs              # Motor de regras FIDE, gerador de lances, validador FEN e exportador PGN
├── EngineService.cs           # Comunicação UCI com Stockfish, motor Minimax interno e classificador de lances
├── StudyLibrary.cs            # Livro de aberturas (Opening Book) e banco com os 15 Puzzles táticos
├── Xadrez.csproj              # Configuração do projeto .NET 8.0 (Windows Forms)
└── README.md                  # Documentação do projeto
```

---

## ⚙️ Pré-requisitos e Configuração do Stockfish

### Requisitos para Compilar Localmente
- **Sistema Operacional:** Windows 10 ou 11 (64 bits).
- **SDK:** [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) instalado.

### Como Configurar o Stockfish (`stockfish.exe`)
O jogo funciona **com ou sem** o executável externo do Stockfish:
1. Para usar o **Stockfish Oficial**, baixe o binário do Stockfish para Windows (`stockfish-windows-x86-64-avx2.exe`), renomeie para **`stockfish.exe`** e coloque-o:
   - Na mesma pasta do executável **`Xadrez.exe`**, **OU**
   - Dentro da subpasta **`assets/stockfish.exe`**.
2. Ao iniciar, a caixa de log inferior indicará `[Stockfish Oficial Conectado]`.
3. Caso o arquivo `stockfish.exe` não seja encontrado, o jogo exibirá `[Engine Tática Posicional Interna Ativa]` e utilizará automaticamente o motor interno em C# sem perda de funcionalidades.

---

## 🚀 Como Executar ou Baixar o Executável (`.exe`)

### Opção 1: Baixar o `.exe` pronto pelo GitHub Actions (Sem instalar nada)
1. Acesse a aba **Actions** deste repositório no GitHub.
2. Clique na execução mais recente do workflow **Gerar Executavel Windows (.exe)**.
3. Role até a seção **Artifacts** no final da página e baixe o arquivo **`Xadrez-Windows-Exe`**.
4. Extraia o arquivo `.zip` (que já inclui o `Xadrez.exe`, o `stockfish.exe` e a pasta `assets/` com as imagens das peças) e execute o **`Xadrez.exe`**.

### Opção 2: Rodar ou Compilar pelo Terminal Local
No terminal (PowerShell ou CMD) na raiz do projeto:

```bash
# Executar em modo de desenvolvimento
dotnet run --project Xadrez.csproj

# Gerar um executável único (Single-File .exe) na pasta ./entrega
dotnet publish Xadrez.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./entrega
```
*(Lembre-se de copiar a pasta `client/assets` para `./entrega/assets` se compilar manualmente).*

---

## 🎮 Guia Rápido de Uso

| Recurso | Como Usar na Interface |
| :--- | :--- |
| **Mover Peça** | Clique esquerdo na sua peça (casas válidas ficam **laranjas**) e clique na casa de destino. |
| **Premove (Jogada Automática)** | Enquanto o bot pensa, clique na sua peça e na casa de destino (fica **azul**). |
| **Demarcar Casa / Criar Seta** | **Clique direito** marca/desmarca a casa; **arrastar com o botão direito** desenha uma seta. |
| **Voltar Lance (*Takeback*)** | Clique no botão **Voltar Lance** na barra superior para desfazer o último par de lances. |
| **Pedir Dica (*Help to Move*)** | Clique em **Ajuda Lance** para que a IA desenhe uma seta verde indicando a melhor jogada. |
| **Analisar Posição Profunda** | Clique em **Start search** (ou no menu `Search & Estudo -> Analisar Posição`) para ver o relatório no painel inferior. |
| **Resolver Puzzles** | Clique no botão **Puzzles** ou escolha um dos 15 desafios no menu superior **Puzzles Táticos**. |
| **Importar / Exportar FEN e PGN** | Use o botão **PGN/FEN** ou acesse o menu superior **File (PGN / FEN)**. |

---

## 🛣️ Roadmap de Evolução Arquitetural

- [ ] Desacoplar o `MainForm.cs` separando a renderização do tabuleiro (`BoardView`) dos controladores de partida, relógio e puzzles.
- [ ] Extrair um serviço dedicado `ChessAnalysisService` com suporte a `MultiPV` nativo no protocolo UCI do Stockfish.
- [ ] Adicionar projeto de testes automatizados (`xUnit`) cobrindo casos extremos de roque, *en passant*, validação de FEN, regra dos 50 lances e repetição tripla.
- [ ] Implementar modo Multiplayer online via WebSockets.

---

## 📄 Licença

Distribuído sob a licença especificada no arquivo [`LICENSE`](./LICENSE) deste repositório.