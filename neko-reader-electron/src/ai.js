/*
 * ai.js - integracao com provedores de IA (Qwen via Alibaba DashScope, ou
 * qualquer endpoint compativel com OpenAI: Ollama, LM Studio, OpenRouter...).
 * Fica no processo principal para contornar o CORS do renderer.
 */
const pdfjs = require('./pdf.js');

const PROVIDERS = {
  qwen: {
    label: 'Qwen (Alibaba DashScope)',
    url: 'https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions',
    model: 'qwen-plus'
  },
  openai_compat: {
    label: 'Compativel OpenAI (Ollama / LM Studio / OpenRouter...)',
    url: null, // definido nas Opcoes
    model: 'qwen2.5:7b'
  }
};

// corta a resposta se vier XML/JSON enrolado (padrao Qwen)
function cleanReply(txt) {
  return String(txt).replace(/<[^>]+>\s*$/g, '').trim();
}

async function chat(settings, messages) {
  const prov = PROVIDERS[settings.aiProvider] || PROVIDERS.qwen;
  const base = settings.aiBaseUrl ? settings.aiBaseUrl.trim() : prov.url;
  if (!base) throw new Error('Defina a URL do servidor de IA nas Opções (aba IA).');
  const key = (settings.aiKey || '').trim();
  if (prov === PROVIDERS.qwen && !key) {
    throw new Error('Chave de API ausente. Crie uma grátis em dashscope.console.aliyun.com e cole em Opções → IA.');
  }
  const model = (settings.aiModel || '').trim() || prov.model;
  const headers = { 'Content-Type': 'application/json' };
  if (key) headers.Authorization = 'Bearer ' + key;

  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), 90000);
  let res;
  try {
    res = await fetch(base, {
      method: 'POST',
      headers,
      signal: ctrl.signal,
      body: JSON.stringify({ model, messages, temperature: 0.3 })
    });
  } catch (e) {
    clearTimeout(timer);
    if (e.name === 'AbortError') throw new Error('A IA demorou mais de 90s (timeout). Tente resumir menos páginas ou use um modelo local.');
    throw new Error('Falha de rede ao falar com a IA: ' + (e.message || e));
  }
  clearTimeout(timer);

  if (!res.ok) {
    const bodyTxt = await res.text().catch(() => '');
    if (res.status === 401 || res.status === 403) throw new Error('Chave de API inválida/expirada (HTTP ' + res.status + '). Confira em Opções → IA.');
    if (res.status === 429) throw new Error('Limite de requisições da IA atingido (HTTP 429). Aguarde um pouco e tente de novo.');
    throw new Error('Erro HTTP ' + res.status + ' do provedor de IA. ' + bodyTxt.slice(0, 200));
  }
  const json = await res.json().catch(() => null);
  const txt = json && json.choices && json.choices[0] && json.choices[0].message && json.choices[0].message.content;
  if (!txt) throw new Error('Resposta vazia/inesperada do provedor de IA.');
  return cleanReply(txt);
}

// extrai texto das paginas [from..to] (0-based, inclusive)
async function extractText(file, from, to) {
  const doc = await pdfjs.getDoc(file);
  const out = [];
  for (let p = Math.max(0, from); p <= Math.min(doc.numPages - 1, to); p++) {
    const page = await doc.getPage(p + 1);
    const tc = await page.getTextContent();
    const s = tc.items.map(i => i.str).join(' ').replace(/\s+/g, ' ').trim();
    out.push({ page: p + 1, text: s });
  }
  return out;
}

function buildMessages(kind, file, name, pages, opts) {
  const chunks = pages.filter(x => x.text.length);
  const hasText = chunks.length > 0;
  const joined = chunks.map(x => `[pág ${x.page}] ${x.text}`).join('\n').slice(0, 60000);
  const range = `páginas ${pages[0].page}-${pages[pages.length - 1].page}`;

  const sys = 'Você é o assistente de leitura embutido no NekoReader, leitor de quadrinhos/mangás/PDF. Responda sempre em português do Brasil, de forma clara e bem estruturada (use markdown leve).';

  let user;
  if (kind === 'summary-doc') {
    return [{ role: 'system', content: sys }, { role: 'user', content: hasText
      ? `Resuma o documento inteiro a seguir ("${name}"). Estruture: visao geral, pontos-chave por secao/pagina e conclusao.\n\n${joined}`
      : `O documento "${name}" nao tem texto extraivel (PDF escaneado). Oriente o usuario a rodar OCR (PowerToys Text Extractor ou DigitalOCR) e entao refazer o resumo.` }];
  }
  if (kind === 'ask-doc') {
    return [{ role: 'system', content: sys }, { role: 'user', content: hasText
      ? `Contexto do documento "${name}":\n\n${joined}\n\nPergunta: ${opts.question}`
      : `Documento "${name}" sem texto (escaneado). Pergunta: "${opts.question}". Peca para rodar OCR e colar o texto.` }];
  }
  if (kind === 'summary') {
    user = hasText
      ? `Resuma o conteúdo a seguir (arquivo "${name}", ${range}). Dê uma visão geral, os pontos-chave em bullets e uma conclusão.\n\n${joined}`
      : `O arquivo "${name}" (${range}) não contém texto extraível — provavelmente é um PDF escaneado (imagem). Explique isso ao usuário e oriente: rode OCR antes (ex.: "PowerToys Text Extractor" na tela, ou DigitalOCR/NAPS2/Tesseract para o arquivo inteiro) e então use "Perguntar sobre esta página" colando o texto reconhecido.`;
  } else { // ask
    user = hasText
      ? `Contexto (arquivo "${name}", ${range}):\n\n${joined}\n\nPergunta do usuário: ${opts.question}`
      : `O trecho "${name}" (${range}) é imagem sem texto (PDF escaneado). Pergunta do usuário: "${opts.question}". Peça que ele rode OCR (PowerToys Text Extractor / DigitalOCR) e cole o texto da página aqui para você responder.`;
  }
  return [{ role: 'system', content: sys }, { role: 'user', content: user }];
}

module.exports = { PROVIDERS, chat, extractText, buildMessages };
