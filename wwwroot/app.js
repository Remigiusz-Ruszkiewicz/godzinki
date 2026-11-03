window.godzinki = {
 online: () => navigator.onLine,
 takeAuthHash: () => { const h=location.hash;if(!h.includes("access_token=")&&!h.includes("error_description="))return null;history.replaceState(null,"",location.pathname+location.search);return h;},
 download: (name,text,type="application/json") => { const u=URL.createObjectURL(new Blob([text],{type}));const a=document.createElement("a");a.href=u;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(u),1000); }
};

