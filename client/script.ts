import { Chess, Square } from 'chess.js';

const game = new Chess();
const boardEl = document.getElementById('board') as HTMLDivElement;
const statusEl = document.getElementById('status') as HTMLDivElement;
const difficultyEl = document.getElementById('difficulty') as HTMLSelectElement;
const resetBtn = document.getElementById('resetBtn') as HTMLButtonElement;

let selectedSquare: Square | null = null;
let legalMovesForSelected: Square[] = [];
let isBotThinking = false;

// Mapeamento exato com os arquivos da sua pasta client/assets
const pieceImages: Record<string, string> = {
  // Peças Brancas
  'wp': './assets/peao 1 branco.png',
  'wr': './assets/torre 1 branca.png',
  'wn': './assets/cavalo 1 branco.png',
  'wb': './assets/bispo 1 branco.png',
  'wq': './assets/rainha branca.png',
  'wk': './assets/rei branco.png',
  // Peças Pretas
  'bp': './assets/peao1 preto.png',
  'br': './assets/torre 1 preto.png',
  'bn': './assets/cavalo 1 preto.png',
  'bb': './assets/bispo1 preto.png',
  'bq': './assets/rainha1preto.png',
  'bk': './assets/rei preto.png',
};

// Configuração dos níveis do Stockfish (Skill Level: 0 a 20 | Depth: profundidade de cálculo)
const difficultySettings: Record<string, { skill: number; depth: number }> = {
  easy:   { skill: 1,  depth: 2 },
  medium: { skill: 7,  depth: 6 },
  hard:   { skill: 14, depth: 11 },
  master: { skill: 20, depth: 16 },
};

// Inicializando o Stockfish via Web Worker (usando CDN para não precisar baixar arquivos pesados)
let stockfish: Worker | null = null;

function initStockfish() {
  const workerCode = `importScripts('https://cdnjs.cloudflare.com/ajax/libs/stockfish.js/10.0.2/stockfish.js');`;
  const blob = new Blob([workerCode], { type: 'application/javascript' });
  stockfish = new Worker(URL.createObjectURL(blob));

  stockfish.postMessage('uci');

  stockfish.onmessage = (event) => {
    const line = event.data;

    // Quando o Stockfish termina de pensar, ele envia: "bestmove e7e5 ..."
    if (typeof line === 'string' && line.startsWith('bestmove')) {
      const match = line.match(/^bestmove\s([a-h][1-8])([a-h][1-8])([qrbn])?/);
      if (match) {
        const [, from, to, promotion] = match;
        game.move({ from, to, promotion: promotion || 'q' });
        isBotThinking = false;
        renderBoard();
        updateStatus();
      }
    }
  };
}

// Pede ao Stockfish para jogar
function makeBotMove() {
  if (game.isGameOver() || !stockfish) return;

  isBotThinking = true;
  updateStatus();

  const level = difficultySettings[difficultyEl.value] || difficultySettings.medium;
  const fen = game.fen();

  stockfish.postMessage(`setoption name Skill Level value ${level.skill}`);
  stockfish.postMessage(`position fen ${fen}`);
  stockfish.postMessage(`go depth ${level.depth}`);
}

// Renderiza o tabuleiro 8x8
function renderBoard() {
  boardEl.innerHTML = '';
  const board = game.board();

  for (let row = 0; row < 8; row++) {
    for (let col = 0; col < 8; col++) {
      const squareName = `${String.fromCharCode(97 + col)}${8 - row}` as Square;
      const isLight = (row + col) % 2 === 0;

      const squareDiv = document.createElement('div');
      squareDiv.className = `square ${isLight ? 'light' : 'dark'}`;
      squareDiv.dataset.square = squareName;

      if (selectedSquare === squareName) {
        squareDiv.classList.add('selected');
      }

      if (legalMovesForSelected.includes(squareName)) {
        squareDiv.classList.add('legal-move');
      }

      const piece = board[row][col];
      if (piece) {
        const key = `${piece.color}${piece.type}`;
        const img = document.createElement('img');
        img.src = pieceImages[key];
        img.alt = key;
        squareDiv.appendChild(img);
      }

      squareDiv.addEventListener('click', () => onSquareClick(squareName));
      boardEl.appendChild(squareDiv);
    }
  }
}

// Lógica de clique nas casas
function onSquareClick(square: Square) {
  if (game.isGameOver() || isBotThinking || game.turn() !== 'w') return;

  // Se clicou em um movimento legal da peça já selecionada, executa o lance
  if (selectedSquare && legalMovesForSelected.includes(square)) {
    game.move({
      from: selectedSquare,
      to: square,
      promotion: 'q', // Promoção automática para Rainha por simplicidade
    });

    selectedSquare = null;
    legalMovesForSelected = [];
    renderBoard();
    updateStatus();

    // Chama o Stockfish após um pequeno intervalo visual
    if (!game.isGameOver()) {
      setTimeout(makeBotMove, 250);
    }
    return;
  }

  // Seleciona uma peça branca do jogador
  const piece = game.get(square);
  if (piece && piece.color === 'w') {
    selectedSquare = square;
    const moves = game.moves({ square, verbose: true });
    legalMovesForSelected = moves.map((m) => m.to as Square);
  } else {
    selectedSquare = null;
    legalMovesForSelected = [];
  }

  renderBoard();
}

// Atualiza o texto de status da partida
function updateStatus() {
  if (game.isCheckmate()) {
    const vencedor = game.turn() === 'w' ? 'Stockfish (Pretas)' : 'Você (Brancas)';
    statusEl.textContent = `Xeque-mate! Vitória de: ${vencedor}`;
  } else if (game.isDraw()) {
    statusEl.textContent = 'Empate!';
  } else if (isBotThinking) {
    statusEl.textContent = 'Stockfish está pensando...';
  } else {
    const checkText = game.inCheck() ? ' (XEQUE!)' : '';
    statusEl.textContent = `Sua vez (Brancas)${checkText}`;
  }
}

// Botão de reiniciar
resetBtn.addEventListener('click', () => {
  game.reset();
  selectedSquare = null;
  legalMovesForSelected = [];
  isBotThinking = false;
  stockfish?.postMessage('ucinewgame');
  renderBoard();
  updateStatus();
});

// Inicialização
initStockfish();
renderBoard();
updateStatus();