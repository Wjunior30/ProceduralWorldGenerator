let engines=[];let selectedEngine='godot';let lastPlan=null;

async function loadEngines(){
  try{engines=await fetch('/api/engines').then(r=>r.json());renderEngineStatus();renderEngineList()}
  catch(error){$('engine-status').textContent='Não foi possível procurar engines instaladas.';console.error(error)}
}
function renderEngineStatus(){
  $('engine-status').innerHTML=engines.filter(e=>e.id!=='generic').map(e=>`<div class="engine-pill ${e.detected?'detected':''}"><span>${e.name}</span><b>${e.status}</b></div>`).join('')
}
function renderEngineList(){
  const root=$('engine-list');root.innerHTML='';
  for(const engine of engines){
    const button=document.createElement('button');button.className=`engine-option ${engine.detected?'detected':''} ${engine.id===selectedEngine?'active':''}`;
    button.innerHTML=`<em>${engine.detected?'DISPONÍVEL':'NÃO DETECTADA'}</em><strong>${engine.name}</strong><small>${engine.capability}</small>`;
    button.onclick=()=>{selectedEngine=engine.id;lastPlan=null;$('consent').checked=false;$('create-project').disabled=true;$('consent-row').classList.add('hidden');$('project-preview').innerHTML='<p>Clique em visualizar para revisar o plano desta engine.</p>';$('open-after').disabled=engine.id==='generic';if(engine.id==='generic')$('open-after').checked=false;renderEngineList()};
    root.appendChild(button)
  }
}
function integrationRequest(confirmed=false){return{engine:selectedEngine,projectName:$('project-name').value.trim(),destination:$('destination').value.trim(),openAfterCreate:$('open-after').checked,overwrite:$('overwrite').checked,confirmed,settings:requestData()}}
function showIntegration(){
  $('integration-modal').classList.remove('hidden');$('integration-result').classList.add('hidden');
  if(!$('project-name').value.trim())$('project-name').value=`Mundo${$('seed').value}`;
  renderEngineList()
}
function closeIntegration(){$('integration-modal').classList.add('hidden')}
async function previewProject(){
  const preview=$('project-preview');preview.innerHTML='<p>Preparando plano de criação...</p>';lastPlan=null;$('consent').checked=false;$('create-project').disabled=true;$('consent-row').classList.add('hidden');
  try{
    const response=await fetch('/api/integrations/preview',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(integrationRequest())});const data=await response.json();if(!response.ok)throw new Error(data.error||'Não foi possível preparar o projeto.');lastPlan=data;
    preview.innerHTML=`<div class="preview-path">DESTINO: ${escapeHtml(data.projectPath)}</div><ul>${data.files.map(file=>`<li>${escapeHtml(file)}</li>`).join('')}</ul>${data.warnings.map(w=>`<div class="preview-warning">ATENÇÃO: ${escapeHtml(w)}</div>`).join('')}`;
    $('consent-row').classList.remove('hidden')
  }catch(error){preview.innerHTML=`<div class="preview-warning">${escapeHtml(error.message)}</div>`}
}
async function createProject(){
  if(!lastPlan||!$('consent').checked)return;
  const button=$('create-project'),result=$('integration-result');button.disabled=true;button.textContent='CRIANDO PROJETO';result.classList.add('hidden');
  try{
    const response=await fetch('/api/integrations/create',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(integrationRequest(true))});const data=await response.json();if(!response.ok)throw new Error(data.error||'Falha ao criar o projeto.');result.className='integration-result';result.innerHTML=`<strong>${escapeHtml(data.message)}</strong><br>${data.filesWritten} arquivos · ID ${data.fingerprint}<br>${escapeHtml(data.projectPath)}`;$('status').textContent=data.opened?'ENGINE ABERTA':'PROJETO CRIADO'
  }catch(error){result.className='integration-result error';result.textContent=error.message}finally{result.classList.remove('hidden');button.textContent='CRIAR E ABRIR NA ENGINE';button.disabled=!$('consent').checked}
}
function escapeHtml(value){const div=document.createElement('div');div.textContent=String(value);return div.innerHTML}

$('integrate').onclick=showIntegration;$('close-integration').onclick=closeIntegration;$('preview-project').onclick=previewProject;$('create-project').onclick=createProject;
$('consent').onchange=()=>{$('create-project').disabled=!$('consent').checked||!lastPlan};$('project-name').oninput=()=>{lastPlan=null;$('consent-row').classList.add('hidden');$('create-project').disabled=true};$('destination').oninput=$('project-name').oninput;$('overwrite').onchange=$('project-name').oninput;
$('integration-modal').onclick=event=>{if(event.target===$('integration-modal'))closeIntegration()};document.addEventListener('keydown',event=>{if(event.key==='Escape')closeIntegration()});
loadEngines().then(()=>{if(new URLSearchParams(location.search).has('integration'))showIntegration()});
