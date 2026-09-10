import { memo, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import './App.css';

// Client ID приложения из старого проекта (см. YandexAuth.jsx в WebPatternTracker).
// В консоли https://oauth.yandex.ru/ должен быть зарегистрирован redirect URI:
//   https://localhost:5173/auth
const YANDEX_CLIENT_ID = '60c4f8e4555340c7ba1acc786b3dd5c2';

// Мозаика-превью с бэкенда: первая страница, ужатая до ~48 клеток.
// Черно-белая: вышитое — черным.
function Preview({ mosaic }) {
    if (!mosaic || mosaic.length === 0) return <div className="preview-empty">Нет данных</div>;

    return (
        <table className="preview">
            <tbody>
                {mosaic.map((row, y) => (
                    <tr key={y}>
                        {row.map((cell, x) => (
                            <td key={x} style={{
                                background: cell.isFinished ? '#000' : '#fff',
                            }} />
                        ))}
                    </tr>
                ))}
            </tbody>
        </table>
    );
}

// Одна клетка полотна: memo + плоские пропсы, чтобы перерисовка
// при закрашивании/подсветке трогала минимум DOM.
// ВАЖНО: компонент объявлен на верхнем уровне (не внутри App),
// иначе memo бесполезен — тип менялся бы каждый рендер.
const PatternCell = memo(function PatternCell({
    x, y, symbol, fontFamily, background, fg,
    cellSize, showSymbol, isHL, onToggle, onSelect,
}) {
    return (
        <td
            title={`(${x},${y}) ${symbol ?? ''}`}
            style={{
                width: cellSize,
                height: cellSize,
                minWidth: cellSize,
                fontSize: Math.max(8, cellSize - 6),
                fontFamily,
                background,
                color: fg,
                boxSizing: 'border-box',
                padding: 0,
                lineHeight: 1,
                verticalAlign: 'middle',
                whiteSpace: 'nowrap',
                overflow: 'hidden',
                textAlign: 'center',
                ...(isHL
                    ? { border: 'none', outline: 'none' }
                    : {
                        borderStyle: 'solid',
                        borderWidth: 0,
                        borderLeftWidth: x % 10 === 0 ? 2 : 1,
                        borderTopWidth: y % 10 === 0 ? 2 : 1,
                        borderLeftColor: x % 10 === 0 ? '#000' : '#bbb',
                        borderTopColor: y % 10 === 0 ? '#000' : '#bbb',
                    }),
            }}
            onContextMenu={e => {
                e.preventDefault();
                onSelect();
            }}
            onClick={onToggle}
        >
            {showSymbol ? symbol : ' '}
        </td>
    );
});

function App() {
    const [auth, setAuth] = useState('checking'); // checking | authorized | unauthorized
    const [patterns, setPatterns] = useState(null);
    const [previews, setPreviews] = useState({});
    const [error, setError] = useState(null);
    const [uploading, setUploading] = useState(false);
    const [job, setJob] = useState(null); // { jobId, currentPage, totalPages }
    const [selectedId, setSelectedId] = useState(null);
    const [detail, setDetail] = useState(null);
    const [selectedColor, setSelectedColor] = useState(-1);
    const [cellSize, setCellSize] = useState(12);
    const [pageLoading, setPageLoading] = useState(false);
    const [showSettings, setShowSettings] = useState(false);
    const fileRef = useRef(null);
    const settingsRef = useRef(null);

    useEffect(() => {
        if (!showSettings) return;
        function onDown(e) {
            if (settingsRef.current && !settingsRef.current.contains(e.target)) {
                setShowSettings(false);
            }
        }
        document.addEventListener('mousedown', onDown);
        return () => document.removeEventListener('mousedown', onDown);
    }, [showSettings]);

    const loadPatterns = useCallback(async () => {
        setError(null);
        try {
            const res = await fetch('/patterns');
            if (res.status === 401) {
                setError('Сессия истекла — войди заново через Яндекс.');
                setPatterns([]);
                return [];
            }
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const list = await res.json();
            setPatterns(list);
            return list;
        } catch (e) {
            setError(String(e.message ?? e));
            setPatterns([]);
            return [];
        }
    }, []);

    const loadPreviews = useCallback(async (list) => {
        const entries = await Promise.all(list.map(async (p) => {
            try {
                const res = await fetch(`/pattern/preview?patternId=${p.id}`);
                if (!res.ok) return [p.id, null];
                return [p.id, await res.json()];
            } catch {
                return [p.id, null];
            }
        }));
        setPreviews(Object.fromEntries(entries));
    }, []);

    const refreshHome = useCallback(async () => {
        const list = await loadPatterns();
        if (list.length > 0) await loadPreviews(list);
        else setPreviews({});
    }, [loadPatterns, loadPreviews]);

    useEffect(() => {
        async function init() {
            // Возврат с oauth.yandex.ru: токен лежит в hash (#access_token=...).
            if (window.location.pathname === '/auth') {
                const hash = new URLSearchParams(window.location.hash.slice(1));
                const accessToken = hash.get('access_token');
                window.history.replaceState(null, '', '/');
                if (!accessToken) {
                    setError('Яндекс не вернул токен.');
                    setAuth('unauthorized');
                    return;
                }
                try {
                    const res = await fetch(`/auth_yandex?${new URLSearchParams({
                        access_token: accessToken,
                        token_type: hash.get('token_type') ?? 'bearer',
                        expires_in: hash.get('expires_in') ?? '',
                    })}`);
                    if (!res.ok) {
                        let detail = `HTTP ${res.status}`;
                        try {
                            const body = await res.json();
                            if (body?.error) detail += `: ${body.error}`;
                        } catch { /* не JSON — показываем статус */ }
                        throw new Error(detail);
                    }
                    setAuth('authorized');
                } catch (e) {
                    setError(`Не удалось войти через Яндекс: ${e.message ?? e}`);
                    setAuth('unauthorized');
                }
                return;
            }

            try {
                const res = await fetch('/user');
                setAuth(res.ok ? 'authorized' : 'unauthorized');
            } catch {
                setAuth('unauthorized');
            }
        }
        init();
    }, []);

    useEffect(() => { if (auth === 'authorized' && selectedId === null) refreshHome(); }, [auth, selectedId, refreshHome]);

    function login() {
        const redirectUri = `${window.location.origin}/auth`;
        window.location.href = `https://oauth.yandex.ru/authorize?${new URLSearchParams({
            response_type: 'token',
            client_id: YANDEX_CLIENT_ID,
            redirect_uri: redirectUri,
            scope: 'login:info',
        })}`;
    }

    async function logout() {
        await fetch('/logout', { method: 'POST' });
        setAuth('unauthorized');
        setPatterns(null);
        setPreviews({});
        setSelectedId(null);
        setDetail(null);
        setSelectedColor(-1);
    }

    function goHome() {
        setSelectedId(null);
        setDetail(null);
        setSelectedColor(-1);
        setFontsLoaded(null);
        setShowSettings(false);
        refreshHome();
    }

    async function pollStatus(jobId) {
        for (;;) {
            await new Promise(r => setTimeout(r, 1000));
            const res = await fetch(`/patterns/upload/status?jobId=${jobId}`);
            if (res.status === 404) throw new Error('Задача потеряна (сервер перезапускался?). Загрузи файл заново.');
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const st = await res.json();
            if (st.status === 'done') return st;
            if (st.status === 'failed') throw new Error(st.error || 'Не удалось распознать схему.');
            setJob({ jobId, currentPage: st.currentPage, totalPages: st.totalPages });
        }
    }

    async function uploadFile(file) {
        if (!file) return;
        const form = new FormData();
        form.append('file', file);
        setUploading(true);
        setError(null);
        try {
            const res = await fetch('/patterns/upload', { method: 'POST', body: form });
            if (!res.ok) {
                const text = await res.text();
                throw new Error(text || `HTTP ${res.status}`);
            }
            const { jobId } = await res.json();
            setJob({ jobId, currentPage: 0, totalPages: 0 });
            const st = await pollStatus(jobId);
            // После загрузки — окно раскладки листов, затем склеенное полотно.
            await askLayout(st.patternId);
        } catch (err) {
            setError(String(err.message ?? err));
        } finally {
            setUploading(false);
            setJob(null);
            if (fileRef.current) fileRef.current.value = '';
        }
    }

    // --- Раскладка листов: столбцы x ряды ---
    const [layoutInfo, setLayoutInfo] = useState(null); // { patternId, sheetCount, sheets, gridCols, gridRows, canvasW, canvasH }
    const [layoutCols, setLayoutCols] = useState(1);
    const [layoutRows, setLayoutRows] = useState(1);
    const [layoutSaving, setLayoutSaving] = useState(false);

    async function askLayout(patternId) {
        setError(null);
        try {
            const res = await fetch(`/pattern/sheets?patternId=${patternId}`);
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const info = await res.json();
            if (info.sheetCount <= 1) {
                // Один лист — раскладка не нужна, сразу полотно.
                await openPattern(patternId);
                return;
            }
            // Дефолт: квадратная сетка (для 60 листов предложит 8x8 — поправь на 12x5).
            const cols = info.gridCols || Math.ceil(Math.sqrt(info.sheetCount));
            const rows = info.gridRows || Math.ceil(info.sheetCount / cols);
            setLayoutCols(cols);
            setLayoutRows(rows);
            setLayoutInfo({ patternId, ...info });
        } catch (err) {
            // Не смогли спросить — открываем как есть.
            await openPattern(patternId);
        }
    }

    function layoutPreview(cols, rows, sheets) {
        // Миниатюра раскладки: номера листов по ячейкам сетки.
        const grid = [];
        for (let r = 0; r < rows; r++) {
            const row = [];
            for (let c = 0; c < cols; c++) {
                const i = r * cols + c;
                row.push(i < sheets.length ? sheets[i].page + 1 : null);
            }
            grid.push(row);
        }
        return grid;
    }

    async function saveLayout() {
        if (!layoutInfo) return;
        const cols = Math.max(1, layoutCols | 0);
        const rows = Math.max(1, layoutRows | 0);
        if (cols * rows < layoutInfo.sheetCount) {
            setError(`Сетка ${cols}×${rows} не вмещает ${layoutInfo.sheetCount} листов — увеличь столбцы или ряды.`);
            return;
        }
        setLayoutSaving(true);
        setError(null);
        try {
            const res = await fetch(`/pattern/layout?patternId=${layoutInfo.patternId}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ cols, rows }),
            });
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            setLayoutInfo(null);
            await openPattern(layoutInfo.patternId);
        } catch (err) {
            setError(String(err.message ?? err));
        } finally {
            setLayoutSaving(false);
        }
    }

    // Нормализация имени шрифта: в PDF встроены сабсеты с тегом
    // ("AAAAAE+FontAwesome"), а извлечённый шрифт / другой парсер могут видеть
    // то же семейство без тега. Стили family строим по нормализованному имени —
    // тогда глиф из схемы, из ключа и загруженный файл находят друг друга.
    function normalizeFont(font) {
        if (!font) return '';
        const m = /^([A-Z0-9]{6})\+(.+)$/.exec(font);
        return m ? m[2] : font;
    }

    // Семейство уникально на схему: один и тот же сабсет-тег
    // в разных PDF может содержать разные глифы.
    // Только [A-Za-z0-9_-]: '+' и прочее CSS может не переварить.
    function fontFamily(patternId, font) {
        return `p${patternId}_${normalizeFont(font).replace(/[^A-Za-z0-9_-]/g, '_')}`;
    }

    // Шрифты именно ключа палитры: отдельный неймспейс (k...),
    // чтобы не пересекаться с сабсет-шрифтами самой схемы.
    function fontFamilyKey(patternId, font) {
        return `k${patternId}_${normalizeFont(font).replace(/[^A-Za-z0-9_-]/g, '_')}`;
    }

    const loadedFontFamilies = useRef(new Set());
    const [fontsLoaded, setFontsLoaded] = useState(null); // { ok, total } для бейджа

    async function loadFontsInto(families, fontsLink, fonts) {
        if (!fontsLink || !fonts) return 0;
        const entries = Object.entries(fonts);
        let ok = 0;
        await Promise.all(entries.map(async ([font, file]) => {
            const family = families(font);
            if (loadedFontFamilies.current.has(family)) { ok++; return; }
            try {
                const face = new FontFace(family, `url(${fontsLink}${encodeURIComponent(file)})`);
                document.fonts.add(face);
                await face.load();
                loadedFontFamilies.current.add(family);
                ok++;
            } catch (e) {
                console.warn('Шрифт не загрузился:', font, e);
            }
        }));
        return ok;
    }

    async function loadPatternFonts(data) {
        if (!data.fontsLink || !data.fonts) { setFontsLoaded(null); return; }
        const ok = await loadFontsInto(f => fontFamily(data.id, f), data.fontsLink, data.fonts);
        setFontsLoaded({ ok, total: Object.keys(data.fonts).length });
    }

    // Стартовый масштаб: по вертикали помещается 50 клеток.
    function fitCellSize70x50() {
        const chromeY = 120;
        const availH = Math.max(200, (window.innerHeight || 800) - chromeY);
        return Math.max(4, Math.min(32, Math.floor(availH / 50)));
    }

    async function openPattern(id) {
        setError(null);
        setPageLoading(true);
        try {
            // Склеенное полотно: бэкенд отдаёт все листы рядом, page всегда 0.
            const res = await fetch(`/pattern/page?patternId=${id}&page=0`);
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data = await res.json();
            await loadPatternFonts(data);
            setSelectedId(data.id);
            setDetail(data);
            setCellSize(fitCellSize70x50());
            setSelectedColor(-1);
        } catch (err) {
            setError(String(err.message ?? err));
        } finally {
            setPageLoading(false);
        }
    }

    async function changeLayout() {
        if (selectedId === null) return;
        await askLayout(selectedId);
    }

    async function toggleCell(cell) {
        if (selectedColor !== -1 && cell.colorId !== selectedColor) return;
        const next = !cell.isFinished;
        // оптимистично обновляем локально
        setDetail(prev => {
            if (!prev) return prev;
            const rows = prev.rows.map(row =>
                row.map(c => (c.x === cell.x && c.y === cell.y) ? { ...c, isFinished: next } : c));
            const colors = prev.colors.map(c =>
                c.colorId === cell.colorId
                    ? { ...c, finishedCells: c.finishedCells + (next ? 1 : -1) }
                    : c);
            return { ...prev, rows, colors };
        });
        try {
            await fetch('/pattern/cell', {
                method: 'PATCH',
                headers: { 'Content-Type': 'application/json' },
                // Координаты глобальные по полотну, page=0: бэкенд сам найдёт лист.
                body: JSON.stringify({ patternId: selectedId, page: 0, x: cell.x, y: cell.y, isFinished: next }),
            });
        } catch (err) {
            setError(String(err.message ?? err));
        }
    }

    const toggleCellCb = useCallback(cell => toggleCell(cell), [selectedId]);
    const selectColorCb = useCallback((colorId) => {
        setSelectedColor(prev => (prev === colorId ? -1 : colorId));
    }, []);

    async function markColor(colorId, isFinished) {
        try {
            const res = await fetch('/pattern/color', {
                method: 'PATCH',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ patternId: selectedId, colorId, isFinished }),
            });
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            await openPattern(selectedId);
        } catch (err) {
            setError(String(err.message ?? err));
        }
    }

    async function removePattern(id) {
        if (!confirm('Удалить схему?')) return;
        await fetch(`/pattern?patternId=${id}`, { method: 'DELETE' });
        if (selectedId === id) { setSelectedId(null); setDetail(null); }
        else refreshHome();
    }

    const rows = detail?.rows ?? [];
    const total = detail?.colors?.reduce((s, c) => s + c.totalCells, 0) ?? 0;
    const done = detail?.colors?.reduce((s, c) => s + c.finishedCells, 0) ?? 0;

    // Карта цветов по colorId: O(1) вместо find() на каждую клетку.
    const colorById = useMemo(() => {
        const m = new Map();
        for (const c of detail?.colors ?? []) m.set(c.colorId, c);
        return m;
    }, [detail?.colors]);
    const flossHexById = useMemo(() => {
        const m = new Map();
        for (const c of detail?.colors ?? []) m.set(c.colorId, c.flossHex);
        return m;
    }, [detail?.colors]);
    const fontById = useMemo(() => {
        const m = new Map();
        for (const c of detail?.colors ?? [])
            m.set(c.colorId, c.font ? fontFamily(detail.id, c.font) : undefined);
        return m;
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [detail?.id, detail?.colors]);
    const symbolById = useMemo(() => {
        const m = new Map();
        for (const c of detail?.colors ?? []) m.set(c.colorId, c.symbol);
        return m;
    }, [detail?.colors]);
    // Подписи-метки на границе каждых 10 клеток (10, 20, 30…):
    // номер висит над линией между клетками, а не в отдельной полосе.
    const rulerMarksX = rows.length > 0 && rows[0].length > 0
        ? rows[0].map((_, gx) => gx + 1).filter(n => n % 10 === 0)
        : [];
    const rulerMarksY = rows.map((_, ry) => ry + 1).filter(n => n % 10 === 0);
    // Отступ под цифры: трём знакам (100+) нужно шире.
    const maxMark = Math.max(0, ...rulerMarksX, ...rulerMarksY);
    const rulerPad = maxMark >= 100 ? 30 : 18;

    function zoom(delta) {
        setCellSize(s => Math.min(48, Math.max(4, s + delta)));
    }

    function cellBackground(cell) {
        const hl = selectedColor !== -1 && cell.colorId === selectedColor;
        if (hl) return cell.isFinished ? '#c2410c' : '#ff8c1a';
        if (cell.isFinished) {
            const hex = flossHexById.get(cell.colorId);
            // Нитка не привязана — светло-серый вместо чёрного.
            return hex || '#d3d3d3';
        }
        return '#fff';
    }

    function cellColor(cell) {
        const hl = selectedColor !== -1 && cell.colorId === selectedColor;
        if (cell.isFinished) return hl ? '#fff' : '#ddd';
        // Оранжевая подсветка невышитых: символ читаем тёмным для контраста.
        if (hl) return '#7c2d12';
        return '#000';
    }

    const [legendSort, setLegendSort] = useState('todo'); // todo | color
    const legendSorted = useMemo(() => {
        if (!detail?.colors) return [];
        return [...detail.colors].sort((a, b) => legendSort === 'color'
            ? a.colorId - b.colorId
            : ((b.totalCells - b.finishedCells) - (a.totalCells - a.finishedCells)) || (a.colorId - b.colorId));
    }, [detail?.colors, legendSort]);

    // --- Нитки: ключ палитры ---
    const [brands, setBrands] = useState([]);
    const [showFloss, setShowFloss] = useState(false);
    const [flossBrand, setFlossBrand] = useState('');
    const [keyFile, setKeyFile] = useState(null);
    const [keyPages, setKeyPages] = useState(null); // [{page, image}]
    const [keySelected, setKeySelected] = useState([]);
    const [keyPairs, setKeyPairs] = useState(null);
    const [keyDebug, setKeyDebug] = useState([]);
    const [keyLoading, setKeyLoading] = useState(false);
    const [keyFonts, setKeyFonts] = useState(null); // { fontsLink, fonts } шрифты PDF-ключа
    const keyFileRef = useRef(null);

    async function openFloss() {
        setShowFloss(true);
        setKeyFile(null);
        setKeyPages(null);
        setKeySelected([]);
        setKeyPairs(null);
        try {
            const res = await fetch('/floss/brands');
            if (res.ok) {
                const list = await res.json();
                setBrands(list);
                if (!flossBrand && list.length > 0) setFlossBrand(list.includes('dmc') ? 'dmc' : list[0]);
            }
        } catch { /* ignore */ }
    }

    async function previewKey(file) {
        if (!file) return;
        setKeyFile(file);
        setKeyPages(null);
        setKeySelected([]);
        setKeyPairs(null);
        setKeyLoading(true);
        setError(null);
        try {
            const form = new FormData();
            form.append('file', file);
            const res = await fetch(`/pattern/${selectedId}/floss-key-preview`, { method: 'POST', body: form });
            if (!res.ok) {
                const text = await res.text();
                throw new Error(text || `HTTP ${res.status}`);
            }
            const data = await res.json();
            setKeyPages(data.pages);
            setKeySelected([]);
        } catch (err) {
            setError(String(err.message ?? err));
        } finally {
            setKeyLoading(false);
        }
    }

    function toggleKeyPage(p) {
        setKeySelected(prev => prev.includes(p) ? prev.filter(x => x !== p) : [...prev, p]);
    }

    async function loadKeyFonts(keyFontsData) {
        if (!keyFontsData?.fontsLink || !keyFontsData?.fonts) return;
        await loadFontsInto(f => fontFamilyKey(selectedId, f), keyFontsData.fontsLink, keyFontsData.fonts);
        setKeyFonts(keyFontsData);
    }

    async function parseKey() {
        if (!keyFile || keySelected.length === 0) return;
        setKeyLoading(true);
        setError(null);
        try {
            const form = new FormData();
            form.append('file', keyFile);
            form.append('brand', flossBrand);
            form.append('pages', keySelected.join(','));
            const res = await fetch(`/pattern/${selectedId}/floss-key`, { method: 'POST', body: form });
            if (!res.ok) {
                const text = await res.text();
                throw new Error(text || `HTTP ${res.status}`);
            }
            const data = await res.json();
            // Бэкенд отдает { pairs, fontsLink, fonts, debug } или (старый) массив пар.
            const pairs = Array.isArray(data) ? data : (data.pairs ?? []);
            setKeyPairs(pairs);
            setKeyDebug(!Array.isArray(data) && Array.isArray(data.debug) ? data.debug : []);
            if (!Array.isArray(data) && data.fontsLink && data.fonts) {
                await loadKeyFonts({ fontsLink: data.fontsLink, fonts: data.fonts });
            } else {
                setKeyFonts(null);
            }
        } catch (err) {
            setError(String(err.message ?? err));
        } finally {
            setKeyLoading(false);
            if (keyFileRef.current) keyFileRef.current.value = '';
        }
    }

    async function saveKeyMap() {
        if (!keyPairs) return;
        try {
            const pairs = Array.isArray(keyPairs) ? keyPairs : (keyPairs.pairs ?? keyPairs);
            const seen = new Set();
            const mappings = [];
            // Дедуп с конца: последний код побеждает (как на бэкенде),
            // отправляем в прямом порядке.
            for (let k = pairs.length - 1; k >= 0; k--) {
                const p = pairs[k];
                if (p.colorId === null || !p.code || seen.has(p.colorId)) continue;
                seen.add(p.colorId);
                mappings.unshift({ colorId: p.colorId, code: p.code });
            }
            if (mappings.length === 0) {
                setError('Сначала сопоставь хотя бы один символ с цветом схемы.');
                return;
            }
            const res = await fetch(`/pattern/${selectedId}/floss-map`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ brand: flossBrand, mappings }),
            });
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            setShowFloss(false);
            setKeyPairs(null);
            await openPattern(selectedId);
        } catch (err) {
            setError(String(err.message ?? err));
        }
    }

    function setPairColor(i, colorId) {
        setKeyPairs(prev => {
            const arr = Array.isArray(prev) ? prev : (prev?.pairs ?? []);
            return arr.map((q, j) =>
                j === i ? { ...q, colorId: colorId < 0 ? null : colorId } : q);
        });
    }

    async function editFlossCode(colorId, brand, code) {
        try {
            const res = await fetch(`/pattern/${selectedId}/floss`, {
                method: 'PATCH',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ colorId, brand, code }),
            });
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            await openPattern(selectedId);
        } catch (err) {
            setError(String(err.message ?? err));
        }
    }

    return (
        <div className="tracker">
            {auth === 'unauthorized' && (
                <div className="auth-row solo">
                    <button onClick={login}>Войти через Яндекс</button>
                </div>
            )}

            {error && <p className="error">{error}</p>}

            {layoutInfo && (
                <div className="modal-backdrop" onClick={() => setLayoutInfo(null)}>
                    <div className="modal" onClick={e => e.stopPropagation()}>
                        <h3>Раскладка листов</h3>
                        <p className="hint">
                            Распознано листов сетки: {layoutInfo.sheetCount}.
                            Укажи, как они лежат в PDF: сколько столбцов и рядов
                            (например, 60 листов = 12 столбцов × 5 рядов).
                        </p>
                        <div className="floss-row">
                            <label>
                                Столбцов:&nbsp;
                                <input
                                    type="number" min={1} max={layoutInfo.sheetCount}
                                    value={layoutCols}
                                    onChange={e => setLayoutCols(Number(e.target.value))}
                                    style={{ width: '4em' }}
                                />
                            </label>
                            <label>
                                Рядов:&nbsp;
                                <input
                                    type="number" min={1} max={layoutInfo.sheetCount}
                                    value={layoutRows}
                                    onChange={e => setLayoutRows(Number(e.target.value))}
                                    style={{ width: '4em' }}
                                />
                            </label>
                            <span className="meta">
                                {layoutCols * layoutRows < layoutInfo.sheetCount
                                    ? `⚠ сетка ${layoutCols}×${layoutRows} не вмещает ${layoutInfo.sheetCount} листов`
                                    : 'листы влезут'}
                            </span>
                        </div>
                        <div className="layout-preview">
                            {layoutPreview(layoutCols, layoutRows, layoutInfo.sheets).map((row, r) => (
                                <div key={r} className="layout-row">
                                    {row.map((n, c) => (
                                        <span key={c} className={n === null ? 'layout-cell empty' : 'layout-cell'}>
                                            {n ?? '·'}
                                        </span>
                                    ))}
                                </div>
                            ))}
                        </div>
                        <div className="floss-row">
                            <button disabled={layoutSaving} onClick={saveLayout}>
                                {layoutSaving ? 'Склеиваю…' : 'Склеить схему'}
                            </button>
                            <button className="link" onClick={() => setLayoutInfo(null)}>Позже</button>
                                </div>
                        </div>
                        </div>
                        )}

            {auth === 'checking' ? (
                <p>Проверяю вход…</p>
            ) : auth !== 'authorized' ? (
                <p>Войди через Яндекс, чтобы увидеть свои схемы.</p>
            ) : selectedId !== null && detail ? (
                <section>
                    <div className="title-row nav-row">
                        <button className="link back" onClick={goHome}>← Назад к схемам</button>
                        <div className="dots-wrap" ref={settingsRef}>
                            <button
                                className="dots-btn"
                                title="Настройки схемы"
                                onClick={() => setShowSettings(v => !v)}
                            >⋯</button>
                            {showSettings && (
                                <div className="dots-menu">
                                    <div className="dots-desc">
                                        <div className="dots-title">{detail.name}</div>
                                        <div>Полотно {detail.width}×{detail.height}</div>
                                        <div>Вышито {done}/{total}</div>
                                        {detail.gridCols && detail.gridRows && (
                                            <div>Листы {detail.gridCols}×{detail.gridRows}</div>
                                        )}
                                        {detail.sheets?.length > 1 && (
                                            <div>Листов: {detail.sheets.length}</div>
                                        )}
                                        {fontsLoaded && (
                                            <div>Шрифты {fontsLoaded.ok}/{fontsLoaded.total}</div>
                                        )}
                                    </div>
                                    <button
                                        className="dots-item"
                                        onClick={() => { setShowSettings(false); openFloss(); }}
                                    >＋ Добавить цвета ниток</button>
                                    {detail.sheets?.length > 1 && (
                                        <button
                                            className="dots-item"
                                            onClick={() => { setShowSettings(false); changeLayout(); }}
                                        >Изменить раскладку</button>
                                    )}
                                    <div className="dots-zoom">
                                        <span>Масштаб</span>
                                        <span className="zoom">
                                            <button onClick={() => zoom(-2)}>−</button>
                                            <span>{cellSize}px</span>
                                            <button onClick={() => zoom(2)}>+</button>
                                        </span>
                                    </div>
                                </div>
                            )}
                        </div>
                    </div>
                    {showFloss && (
                        <div className="modal-backdrop" onClick={() => setShowFloss(false)}>
                            <div className="modal" onClick={e => e.stopPropagation()}>
                                <h3>Цвета ниток</h3>
                                <div className="floss-row">
                                    <label>
                                        Нитки:&nbsp;
                                        <select value={flossBrand} onChange={e => setFlossBrand(e.target.value)}>
                                            {brands.map(b => <option key={b} value={b}>{b.toUpperCase()}</option>)}
                                        </select>
                                    </label>
                                    <input
                                        ref={keyFileRef}
                                        type="file"
                                        accept="application/pdf"
                                        onChange={e => previewKey(e.target.files?.[0])}
                                    />
                                </div>
                                <p className="hint">Загрузи PDF с ключом палитры, отметь страницы с палитрой и нажми «Распознать». Символ и номер считаются сами, бренд возьми из списка. Несопоставленное поправь руками в легенде.</p>
                                {keyLoading && <p>Читаю…</p>}
                                {keyPages && (
                                    <>
                                        <div className="key-pages">
                                            {keyPages.map(p => (
                                                <label key={p.page} className={keySelected.includes(p.page) ? 'key-page sel' : 'key-page'}>
                                                    <input
                                                        type="checkbox"
                                                        checked={keySelected.includes(p.page)}
                                                        onChange={() => toggleKeyPage(p.page)}
                                                    />
                                                    <img src={p.image} alt={`стр. ${p.page}`} />
                                                    <span>стр. {p.page}</span>
                                                </label>
                                            ))}
                                        </div>
                                        <button disabled={keySelected.length === 0 || keyLoading} onClick={parseKey}>
                                            Распознать выбранные ({keySelected.length})
                                        </button>
                                    </>
                                )}
                                {(Array.isArray(keyPairs) ? keyPairs.length > 0 : (keyPairs?.pairs?.length > 0)) && (
                                    <>
                                        {keyDebug.length > 0 && (
                                            <p className="hint">Распознано: {keyDebug.join(' · ')}</p>
                                        )}
                                        <table className="key-table">
                                            <thead>
                                                <tr><th>Символ</th><th>Номер</th><th>Цвет нитки</th></tr>
                                            </thead>
                                            <tbody>
                                                {(Array.isArray(keyPairs) ? keyPairs : (keyPairs?.pairs ?? [])).map((p, i) => (
                                                    <tr key={i}>
                                                        <td className="key-symbol" title={p.raw || undefined} style={p.font ? { fontFamily: fontFamilyKey(selectedId, p.font) } : undefined}>
                                                            {p.symbol || <span className="key-no-sym" title="Значок нарисован кривыми, а не шрифтом — номер правится вручную">◇</span>}
                                                        </td>
                                                        <td>
                                                            <input
                                                                value={p.code}
                                                                onChange={e => setKeyPairs(prev => {
                                                                    const arr = Array.isArray(prev) ? prev : (prev?.pairs ?? []);
                                                                    return arr.map((q, j) => j === i ? { ...q, code: e.target.value } : q);
                                                                })}
                                                            />
                                                        </td>
                                                        <td>{p.hex && <span className="swatch" style={{ background: p.hex }} />}</td>
                                                    </tr>
                                                ))}
                                            </tbody>
                                        </table>
                                        <button onClick={saveKeyMap}>Сохранить привязку</button>
                                    </>
                                )}
                                <button className="link" onClick={() => setShowFloss(false)}>Закрыть</button>
                            </div>
                        </div>
                    )}
                    {selectedColor !== -1 && (
                        <div className="pages-row">
                            <span className="hl">Выбран цвет {selectedColor}</span>
                            <button onClick={() => markColor(selectedColor, true)}>Закрасить весь цвет</button>
                            <button onClick={() => markColor(selectedColor, false)}>Снять весь цвет</button>
                            <button onClick={() => setSelectedColor(-1)}>Сбросить выбор</button>
                        </div>
                    )}
                    <div className="viewer">
                        {pageLoading ? (
                            <p>Загружаю полотно…</p>
                        ) : (
                        <div className="canvas-scroll">
                            <div className="canvas-inner">
                            <div className="ruler-top" style={{ marginLeft: rulerPad, height: rulerPad, width: (rows[0]?.length || 0) * cellSize }}>
                                {rulerMarksX.map(n => (
                                    <span
                                        key={n}
                                        className="ruler-mark"
                                        style={{ left: n * cellSize, fontSize: Math.max(9, cellSize - 6) }}
                                    >
                                        {n}
                                    </span>
                                ))}
                            </div>
                            <div className="canvas-body">
                                <div className="ruler-left" style={{ width: rulerPad }}>
                                    {rulerMarksY.map(n => (
                                        <span
                                            key={n}
                                            className="ruler-mark"
                                            style={{ top: n * cellSize, fontSize: Math.max(9, cellSize - 6) }}
                                        >
                                            {n}
                                        </span>
                                    ))}
                                </div>
                        <table className="pattern-table">
                            <tbody>
                                {rows.map((row, ry) => (
                                    <tr key={row[0]?.y ?? ry}>
                                        {row.map(cell => {
                                            const isHL = cell.isFinished || (selectedColor !== -1 && cell.colorId === selectedColor);
                                            const hlSel = selectedColor !== -1 && cell.colorId === selectedColor;
                                            const hex = flossHexById.get(cell.colorId);
                                            const background = hlSel
                                                ? (cell.isFinished ? '#c2410c' : '#ff8c1a')
                                                : cell.isFinished
                                                    ? (hex || '#d3d3d3')
                                                    : '#fff';
                                            const fg = cell.isFinished
                                                ? (hlSel ? '#fff' : '#ddd')
                                                : hlSel ? '#7c2d12' : '#000';
                                            const symbol = symbolById.get(cell.colorId);
                                            return (
                                                <PatternCell
                                                    key={cell.x}
                                                    x={cell.x}
                                                    y={cell.y}
                                                    symbol={symbol === 'EMPTY' ? ' ' : symbol}
                                                    fontFamily={fontById.get(cell.colorId)}
                                                    background={background}
                                                    fg={fg}
                                                    cellSize={cellSize}
                                                    showSymbol={!cell.isFinished}
                                                    isHL={isHL}
                                                    onToggle={() => toggleCellCb(cell)}
                                                    onSelect={() => selectColorCb(cell.colorId)}
                                                />
                                            );
                                        })}
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                            </div>
                        </div>
                        </div>
                        )}
                        <aside className="legend">
                            <h3>Цвета</h3>
                            <p className="hint">Правый клик по клетке — подсветить цвет. Левый — закрасить (только выбранный цвет).</p>
                            <div className="legend-sort">
                                <span>Сорт:</span>
                                <button
                                    className={legendSort === 'todo' ? 'link active' : 'link'}
                                    onClick={() => setLegendSort('todo')}
                                    title="Сначала с большим остатком невышитых"
                                >остаток ↓</button>
                                <button
                                    className={legendSort === 'color' ? 'link active' : 'link'}
                                    onClick={() => setLegendSort('color')}
                                    title="Исходный порядок цветов схемы"
                                >по схеме</button>
                            </div>
                            <ul>
                                {legendSorted
                                    .map(c => (
                                    <li key={c.colorId} className={c.colorId === selectedColor ? 'active' : ''}>
                                        <span
                                            className="swatch"
                                            style={{ background: c.flossHex || '#fff' }}
                                            title={c.flossBrand && c.flossCode ? `${c.flossBrand.toUpperCase()} ${c.flossCode}` : 'нитка не привязана'}
                                        />
                                        <button className="link" onClick={() => setSelectedColor(prev => (prev === c.colorId ? -1 : c.colorId))}>
                                            <span className="color-sym" style={c.font ? { fontFamily: fontFamily(detail.id, c.font) } : undefined}>
                                                {c.symbol}
                                            </span>
                                        </button>
                                        <span title={`Вышито ${c.finishedCells} из ${c.totalCells}`}> {c.totalCells - c.finishedCells}</span>
                                        <input
                                            className="code-input"
                                            placeholder="№"
                                            title="Номер ниток (Enter — сохранить)"
                                            defaultValue={c.flossCode || ''}
                                            key={`${c.colorId}-${c.flossCode || ''}`}
                                            onKeyDown={e => {
                                                if (e.key === 'Enter') {
                                                    editFlossCode(c.colorId, c.flossBrand || flossBrand || 'dmc', e.target.value);
                                                }
                                            }}
                                        />
                                    </li>
                                ))}
                            </ul>
                        </aside>
                    </div>
                </section>
            ) : (
                <section>
                    <h2>Мои схемы</h2>
                    {patterns === null ? (
                        <p>Загрузка…</p>
                    ) : (
                        <div className="cards">
                            {patterns.map(p => (
                                <div key={p.id} className="card" onClick={() => openPattern(p.id)}>
                                    <Preview mosaic={previews[p.id]?.mosaic} />
                                    <div className="card-body">
                                        <div className="card-title">{p.name}</div>
                                        {p.sourceFileName && (
                                            <div className="meta file">файл: {p.sourceFileName}</div>
                                        )}
                                        <div className="meta">{p.finishedCells}/{p.totalCells} · {p.width}×{p.height}</div>
                                        <div className="progress-track small">
                                            <div
                                                className="progress-bar"
                                                style={{ width: p.totalCells ? `${Math.round((p.finishedCells / p.totalCells) * 100)}%` : '0%' }}
                                            />
                                        </div>
                                    </div>
                                    <button
                                        className="danger card-delete"
                                        title="Удалить схему"
                                        onClick={e => { e.stopPropagation(); removePattern(p.id); }}
                                    >✕</button>
                                </div>
                            ))}
                            <div
                                className="card add-card"
                                onClick={() => !uploading && fileRef.current?.click()}
                            >
                                <input
                                    ref={fileRef}
                                    type="file"
                                    accept="application/pdf"
                                    hidden
                                    onChange={e => uploadFile(e.target.files?.[0])}
                                />
                                {uploading && job && job.totalPages > 0 ? (
                                    <div className="progress">
                                        <div className="progress-text">
                                            Страница {Math.max(job.currentPage, 1)} из {job.totalPages}…
                                        </div>
                                        <div className="progress-track">
                                            <div
                                                className="progress-bar"
                                                style={{ width: `${Math.round((job.currentPage / job.totalPages) * 100)}%` }}
                                            />
                                        </div>
                                    </div>
                                ) : (
                                    <>
                                        <div className="plus">+</div>
                                        <div className="meta">{uploading ? 'Загружаю файл…' : 'Загрузить PDF-схему'}</div>
                                    </>
                                )}
                            </div>
                        </div>
                    )}
                </section>
            )}
        </div>
    );
}

export default App;
