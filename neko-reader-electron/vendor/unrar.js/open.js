/* Placeholder opcional para unrar.js (WASM). Para habilitar RAR sem dependencias externas:
 *   1) baixe a build do unrar.js (https://github.com/kelvinmacnz/unrar.js ou danielgindi/unrar-wasm)
 *   2) coloque os arquivos .js/.wasm nesta pasta vendor/unrar.js/
 *   3) implemente listRAR(buffer) -> { entries:[{filename}] } e unRARAll(buffer) -> { files:[{name,content}] }
 * Enquanto nao houver build aqui, o leitor cai no fallback 7z.exe/UnRAR.exe com mensagem clara.
 */
async function listRAR() { throw new Error('unrar.js (WASM) nao instalado em vendor/unrar.js'); }
async function unRARAll() { throw new Error('unrar.js (WASM) nao instalado em vendor/unrar.js'); }
module.exports = { listRAR, unRARAll };
