const fs = require('fs');
const vm = require('vm');
const assert = require('assert/strict');
const source = fs.readFileSync('Pages/Panel.cshtml', 'utf8');
// Parse the complete page script, and exercise the actual viewer code with a minimal DOM.
const full = source.match(/<script>([\s\S]*?)<\/script>/)[1];
new vm.Script(full);
const start = source.indexOf("            const photoDialog =");
const end = source.indexOf("            const photoFile =", start);
async function scenario(remaining, elapsed = 50, hidden = false) {
    let callback, delay, refreshed = 0, revoked = 0;
    const handlers = {};
    const image = { removeAttribute() { delete this.src; }, addEventListener(name, fn) { handlers[name] = fn; } };
    const dialog = { open: false, showModal() { this.open = true; }, close() { this.open = false; handlers.close?.(); }, addEventListener(name, fn) { handlers[name] = fn; } };
    const label = {};
    const document = { hidden, getElementById(id) { return ({'photo-dialog':dialog, 'single-photo':image, 'photo-close':{}, 'photo-view-label':label})[id]; } };
    let tick = 0;
    const context = vm.createContext({document, URL:{createObjectURL:()=> 'blob:test',revokeObjectURL:()=>revoked++}, performance:{now:()=>tick++ ? elapsed : 0}, setTimeout(fn, ms){callback=fn;delay=ms;return 1}, clearTimeout(){callback=null}, root:{dataset:{photoUrl:'/Panel?handler=Photo'}}, sendStatus:{}, actionBody:()=>({append(){}}), refresh:async()=>refreshed++, fetch:async()=>({ok:true,headers:{get:()=>remaining},blob:async()=>({})})});
    vm.runInContext(source.slice(start,end),context);
    const control={disabled:false};
    await vm.runInContext('openPhoto(1, control)',Object.assign(context,{control}));
    assert.equal(control.disabled,false);
    assert.equal(refreshed,1);
    return {dialog,image,label,get callback(){return callback},delay,context,get revoked(){return revoked}};
}
(async()=>{
    let r=await scenario(null); assert.equal(r.dialog.open,true);assert.equal(r.callback,null);assert.equal(r.label.textContent,'Tek görüntülemelik fotoğraf');
    r=await scenario('1000');assert.equal(r.dialog.open,true);assert.equal(r.delay,950);r.callback();assert.equal(r.dialog.open,false);assert.equal(r.image.src,undefined);assert.equal(r.revoked,1);
    r=await scenario('40',50);assert.equal(r.dialog.open,false);assert.equal(r.image.src,undefined);
    r=await scenario('1000',50,true);assert.equal(r.dialog.open,false);
    r=await scenario('1000');r.dialog.close();assert.equal(r.callback,null);assert.equal(r.image.src,undefined);
    console.log('PASS: complete page JavaScript syntax; viewer once/timed expiry, elapsed request time, hidden page and manual close.');
})().catch(error=>{console.error(error);process.exitCode=1});

// Exercise actual rendering code: missing/expired audio must not create an audio request.
const renderStart = source.indexOf('            function renderMedia(');
const renderEnd = source.indexOf('            for (const [id, node] of nodes)', renderStart);
function renderFixture(message) {
    const children = [];
    const content = { dataset:{}, classList:{add(){},toggle(){}}, replaceChildren(){children.length=0}, append(...items){children.push(...items)} };
    const document = { createElement(tag){return {tag, attributes:{},setAttribute(k,v){this.attributes[k]=v},addEventListener(){},append(){}}} };
    const context = vm.createContext({document, root:{dataset:{audioUrl:'/Panel?handler=Audio'}}, person:2, formatDuration:()=> '00:02', node:{querySelector:()=>content}, message});
    vm.runInContext(source.slice(renderStart, renderEnd), context);
    vm.runInContext('renderMedia(node, message)', context);
    return {content,children};
}
for (const [expired, available, expected] of [[true,false,'Ses kaydının süresi doldu'],[false,false,'Ses kaydı artık mevcut değil']]) {
    const r=renderFixture({id:1,kind:1,expired,available});assert.equal(r.content.textContent,expected);assert.equal(r.children.length,0);
}
const playable=renderFixture({id:1,kind:1,expired:false,available:true});
assert.equal(playable.children[0].src,'/Panel?handler=Audio&id=1');assert.equal(playable.children[0].attributes.controlsList,'nodownload');
assert.equal(renderFixture({id:1,kind:2,viewed:false,expired:false,available:false}).content.textContent,'Fotoğraf artık mevcut değil');
console.log('PASS: missing/expired audio and photo rendering; authorized audio URL and no-download controls preserved.');
