window.godzinki = {
 getTheme: () => document.documentElement.dataset.theme || 'dark',
 setTheme: theme => {document.documentElement.dataset.theme=theme;try{localStorage.setItem('godzinki.theme',theme);}catch{}document.querySelector('meta[name="theme-color"]')?.setAttribute('content',theme==='dark'?'#030407':'#f6f7f2');},
 online: () => navigator.onLine,
 takeAuthHash: () => { const h=location.hash;if(!h.includes("access_token=")&&!h.includes("error_description="))return null;history.replaceState(null,"",location.pathname+location.search);return h;},
 download: (name,text,type="application/json") => { const u=URL.createObjectURL(new Blob([text],{type}));const a=document.createElement("a");a.href=u;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(u),1000); }
};
window.godzinki.setTheme(window.godzinki.getTheme());

