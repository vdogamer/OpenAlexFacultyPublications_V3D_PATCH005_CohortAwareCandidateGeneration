import fs from 'node:fs/promises';
import http from 'node:http';
import assert from 'node:assert/strict';
import { chromium } from 'playwright';
const repo=process.cwd();
const out=process.env.UI_CHECK_OUTPUT || '/tmp/openalex-frontend-checks';
await fs.mkdir(out,{recursive:true});
const template=await fs.readFile(repo+'/src/OpenAlex.Web/Pages/Shared/_Layout.cshtml','utf8');
const previousPaths=["/","/Faculty","/Faculty/MyPublications","/Admin/AddFaculty","/Review/Identities","/Review/Authors","/Review/Publications","/Reports/AcceptedPublications","/Reports/DataProvenance","/Reports/AcademicOnlineFacultyLookup","/Reports/FacultyDois","/Reports/SharedPublications","/Reports/DepartmentPublications","/Operations","/Operations/Runs","/Admin/ExecutionWindows","/Admin/Scheduler","/Admin/AcademicOnlineDepartments","/Admin/Jobs","/Admin/Preflight","/Admin/Deployment","/Admin/Roles","/Admin/Users","/Account/Login"];
const css=await fs.readFile(repo+'/src/OpenAlex.Web/wwwroot/css/site.css','utf8');
const js=await fs.readFile(repo+'/src/OpenAlex.Web/wwwroot/js/site-navigation.js','utf8');
const doi=await fs.readFile(repo+'/src/OpenAlex.Web/Pages/Reports/FacultyDois.cshtml','utf8');
const body=doi.slice(doi.indexOf('<h1>'),doi.indexOf('@if (Model.ErrorMessage')).replace('@Model.UserId','').replace('@@virginia.edu','@virginia.edu');
function render(path,authenticated=true){
 let html=template.slice(template.indexOf('<!doctype'));
 html=html.replace(/@if \(User\?\.Identity\?\.IsAuthenticated == true\)\s*\{([\s\S]*?)\n\s*\}\s*else\s*\{([\s\S]*?)\n\s*\}/g,(_,yes,no)=>authenticated?yes:no);
 html=html.replace(/@if \(User\?\.Identity\?\.IsAuthenticated == true\)\s*\{([\s\S]*?)\n\s*\}/g,(_,yes)=>authenticated?yes:'');
 html=html.replace(/@User\.Identity\.Name/g,'admin.demo');
 html=html.replace(/@\(ViewData\["Title"\] \?\? "Faculty Publications"\)/g,'Faculty DOI Lookup');
 html=html.replace(/@\(IsWithin\((.*?)\) \? "is-active" : null\)/g,(_,routes)=>[...routes.matchAll(/"([^"]+)"/g)].some(m=>path===m[1]||path.startsWith(m[1]+'/'))?'is-active':'');
 html=html.replace(/ aria-current="@\(IsPage\("([^"]+)"\) \? "page" : null\)"/g,(_,url)=>path.replace(/\/$/,'')===url.replace(/\/$/,'')?' aria-current="page"':'');
 html=html.replace(/(["'])~\//g,'$1/').replace(/ asp-append-version="true"/g,'').replace('@RenderBody()',body);
 assert(!html.includes('@(')&&!html.includes('@if'),'Unprocessed Razor fixture expression');
 return html;
}
const server=http.createServer((req,res)=>{
 const url=new URL(req.url,'http://localhost');
 if(url.pathname==='/css/site.css'){res.setHeader('Content-Type','text/css');res.end(css);}
 else if(url.pathname==='/js/site-navigation.js'){res.setHeader('Content-Type','application/javascript');res.end(js);}
 else{res.setHeader('Content-Type','text/html');res.end(render(url.pathname,!url.searchParams.has('anonymous')));}
});
await new Promise(r=>server.listen(0,'127.0.0.1',r));
const base='http://127.0.0.1:'+server.address().port;
const browser=await chromium.launch({headless:true});
const page=await browser.newPage();
const errors=[];page.on('pageerror',e=>errors.push(e.message));
const widths=[1920,1366,1024,841,840,768,640,390,320];
for(const width of widths){
 await page.setViewportSize({width,height:1000});await page.goto(base+'/Reports/FacultyDois');
 assert.equal(await page.locator('.topbar').evaluate(el=>getComputedStyle(el).backgroundColor),'rgb(35, 45, 75)');
 assert.equal(await page.locator('a[aria-current="page"]').getAttribute('href'),'/Reports/FacultyDois');
 assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'Horizontal overflow at '+width);
 const toggle=page.locator('.nav-toggle');
 if(width<=840){
  assert(await toggle.isVisible());assert(!(await page.getByRole('link',{name:'DOI Lookup',exact:true}).isVisible()));
  await toggle.click();assert.equal(await toggle.getAttribute('aria-expanded'),'true');
  assert(await page.getByRole('link',{name:'DOI Lookup',exact:true}).isVisible());
 }else assert(!(await toggle.isVisible()));
 await page.locator('.nav-group').filter({has:page.locator('summary',{hasText:'Publications'})}).locator('summary').click();
 assert.equal(await page.locator('.nav-group[open]').count(),1);
 assert(await page.getByRole('link',{name:'Accepted publications',exact:true}).isVisible());
 await page.locator('summary').filter({hasText:'Administration'}).click();
 await page.waitForTimeout(40);
 assert.equal(await page.locator('.nav-group[open]').count(),1);
 const box=await page.locator('.nav-group[open] .nav-dropdown').boundingBox();
 assert(box.x>=0&&box.x+box.width<=width,'Dropdown exceeds viewport '+width);
 await page.keyboard.press('Escape');
 assert.equal(await page.locator('.nav-group[open]').count(),0);
 assert.equal(await page.locator(':focus').textContent().then(x=>x.trim()),'Administration');
 await page.locator('summary').filter({hasText:'Review',exact:true}).click();
 await page.locator('h1').click();assert.equal(await page.locator('.nav-group[open]').count(),0);
}
await page.setViewportSize({width:1366,height:1000});await page.goto(base+'/Reports/FacultyDois');
const paths=await page.locator('.primary-nav a, .authbar a').evaluateAll(a=>a.map(el=>el.getAttribute('href')));
for(const path of previousPaths.filter(p=>p!=='/Account/Login'))assert(paths.includes(path),'Lost route '+path);
await page.locator('summary').filter({hasText:'Faculty',exact:true}).focus();await page.keyboard.press('Enter');
assert(await page.getByRole('link',{name:'Faculty directory',exact:true}).isVisible());
await page.keyboard.press('Tab');assert.equal(await page.locator(':focus').textContent(),'Faculty directory');
await page.keyboard.press('Escape');assert.equal(await page.locator(':focus').textContent().then(x=>x.trim()),'Faculty');
await page.goto(base+'/Admin/Jobs');assert.equal(await page.locator('.nav-group.is-active summary').textContent().then(x=>x.trim()),'Operations');
await page.goto(base+'/Reports/FacultyDois?anonymous=1');
assert(await page.getByRole('link',{name:'Log in',exact:true}).isVisible());assert.equal(await page.getByRole('link',{name:'My publications',exact:true}).count(),0);
await page.goto(base+'/Reports/FacultyDois');
await page.screenshot({path:out+'/desktop.png',fullPage:true});
await page.locator('summary').filter({hasText:'Publications',exact:true}).click();
await page.screenshot({path:out+'/desktop-menu.png',fullPage:true});
await page.setViewportSize({width:390,height:1000});await page.goto(base+'/Reports/FacultyDois');
await page.locator('.nav-toggle').click();await page.locator('summary').filter({hasText:'Faculty',exact:true}).click();
await page.screenshot({path:out+'/mobile.png',fullPage:true});
await page.setViewportSize({width:1366,height:1000});
assert.equal(await page.locator('.nav-toggle').getAttribute('aria-expanded'),'false');
const nojs=await browser.newContext({javaScriptEnabled:false,viewport:{width:390,height:1000}});
const fallback=await nojs.newPage();await fallback.goto(base+'/Reports/FacultyDois');
assert(await fallback.getByRole('link',{name:'DOI Lookup',exact:true}).isVisible());
await fallback.locator('summary').filter({hasText:'Publications',exact:true}).click();assert(await fallback.getByRole('link',{name:'Accepted publications',exact:true}).isVisible());
assert.equal(errors.length,0,errors.join('\n'));
await fs.writeFile(out+'/browser-checks.json',JSON.stringify({widths,routeCount:previousPaths.length,keyboard:'Enter, Tab, Escape',outsideClick:true,signedOut:true,noJavaScriptFallback:true,javascriptErrors:errors,scope:'Browser fixture generated from actual Razor layout, stylesheet, navigation script and initial DOI page. Not a live ASP.NET/SQL test.'},null,2));
console.log('Browser checks passed at '+widths.join(', ')+' px; preserved all '+previousPaths.length+' navigation routes.');
await browser.close();server.close();
