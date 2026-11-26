(() => {
'use strict';
const digits = value => String(value).replace(/[0-9]/g, d => '۰۱۲۳۴۵۶۷۸۹'[d]);
document.querySelectorAll('input[type="password"]').forEach((input,index) => {
 if (!input.id) input.id = `password-${index}`;
 const wrap=document.createElement('div'); wrap.className='password-field'; input.before(wrap); wrap.append(input);
 const button=document.createElement('button'); button.type='button'; button.className='password-toggle';
 button.setAttribute('aria-controls',input.id); button.setAttribute('aria-pressed','false'); button.setAttribute('aria-label','نمایش رمز عبور');
 button.innerHTML='<svg aria-hidden="true" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M2 12s3-7 10-7 10 7 10 7-3 7-10 7S2 12 2 12Z"/><circle cx="12" cy="12" r="3"/></svg>';
 button.addEventListener('click',()=>{const show=input.type==='password'; input.type=show?'text':'password'; button.setAttribute('aria-pressed',String(show));button.setAttribute('aria-label',show?'پنهان کردن رمز عبور':'نمایش رمز عبور');}); wrap.append(button);
});
document.querySelectorAll('[data-price]').forEach(input=>{
 const format=()=>{const raw=input.value.replace(/[۰-۹]/g,d=>'۰۱۲۳۴۵۶۷۸۹'.indexOf(d)).replace(/[٠-٩]/g,d=>'٠١٢٣٤٥٦٧٨٩'.indexOf(d)).replace(/[,٬]/g,'');if (/^\d+$/.test(raw)) input.value=digits(raw.replace(/^0+(?=\d)/,'').replace(/\B(?=(\d{3})+(?!\d))/g,'٬'));};format();input.addEventListener('blur',format);
});
// Close the navigation before showing filters, then restore focus to its trigger.
const filterPanel=document.getElementById('product-filters');
const filterTrigger=document.querySelector('.mobile-filter-trigger');
if(filterPanel&&filterTrigger){
 filterPanel.addEventListener('show.bs.offcanvas',()=>{
  const navigation=document.getElementById('navigation');
  if(navigation.classList.contains('show')) bootstrap.Collapse.getOrCreateInstance(navigation).hide();
 });
 filterPanel.addEventListener('hidden.bs.offcanvas',()=>{
  const toggler=document.querySelector('.navbar-toggler');
  if(window.innerWidth<992) toggler.focus();
 });
}
const badge=document.getElementById('unread-badge'), readForm=document.getElementById('read-messages');let busy=false,readDone=false;
async function refresh(){
 if(!badge||document.visibilityState!=='visible'||busy)return;busy=true;
 try{
 // Acknowledge only the messages rendered on this visible page.
 if(readForm&&!readDone){const r=await fetch(readForm.action,{method:'POST',body:new FormData(readForm),credentials:'same-origin'});readDone=r.ok;}
 const r=await fetch(badge.dataset.url,{cache:'no-store',credentials:'same-origin',headers:{Accept:'application/json'}});
 if(!r.ok||!r.headers.get('content-type')?.includes('application/json'))return;
 const {count}=await r.json();badge.hidden=count===0;badge.textContent=count>99?'۹۹+':digits(count);badge.setAttribute('aria-label',`${digits(count)} پیام خوانده‌نشده`);
 }catch{}finally{busy=false;}
}
refresh();if(badge){setInterval(refresh,30000);document.addEventListener('visibilitychange',refresh);window.addEventListener('focus',refresh);}
})();
