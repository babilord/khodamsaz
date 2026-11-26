import asyncio, json, os, sys
from pathlib import Path
from playwright.async_api import async_playwright
ROOT=(sys.argv[1] if len(sys.argv)>1 else 'http://localhost:5262').rstrip('/')
ARTIFACTS=Path('.artifacts'); ARTIFACTS.mkdir(exist_ok=True)
async def main():
 async with async_playwright() as p:
  browser=await p.chromium.launch(channel=os.environ.get('BROWSER_CHANNEL', 'msedge'),headless=True)
  page=await browser.new_page()
  errors=[]
  page.on('pageerror',lambda e:errors.append(str(e)))
  results=[]
  for w,h in [(320,700),(360,800),(375,812),(390,844),(414,896),(768,1024),(1366,768)]:
   await page.set_viewport_size({'width':w,'height':h})
   await page.goto(ROOT+'/'); await page.evaluate('document.fonts.ready')
   assert await page.locator('.brand').inner_text()=='خانه'
   assert not await page.evaluate('document.documentElement.scrollWidth>innerWidth'),w
   cards=await page.locator('.listing-card').evaluate_all('(cs)=>cs.map(c=>{const r=c.getBoundingClientRect();return {x:r.x,y:r.y,w:r.width,h:r.height,overflow:c.scrollWidth>c.clientWidth}})')
   assert len(cards)>=4
   assert abs(cards[0]['y']-cards[1]['y'])<1
   if w<768: assert cards[2]['y']>cards[0]['y']
   if w>=1200: assert abs(cards[0]['y']-cards[2]['y'])<1
   assert not any(c['overflow'] for c in cards)
   assert await page.locator('.hero h1').is_visible()
   assert await page.locator('.price').first.evaluate('(e)=>/[۰-۹]/.test(e.textContent)')
   if w<992:
    assert not await page.locator('#q').is_visible()
    await page.locator('.navbar-toggler').click()
    await page.locator('.mobile-filter-trigger').click()
    await page.locator('#product-filters').wait_for(state='visible')
    await page.locator('#q').fill('گردنبند')
    await page.keyboard.press('Escape'); await page.locator('#product-filters').wait_for(state='hidden')
    assert page.url==ROOT+'/'
    await page.locator('.navbar-toggler').click(); await page.locator('.mobile-filter-trigger').click()
    await page.locator('#q').wait_for(state='visible'); assert await page.locator('#q').input_value()=='گردنبند'
    await page.locator('.product-filter-form button').click(); await page.wait_for_url('**/*Q=*')
    assert len(await page.locator('.listing-card').all())==1
    await page.locator('.navbar-toggler').click(); await page.locator('.mobile-filter-trigger').click()
    await page.locator('#q').wait_for(state='visible'); assert await page.locator('#q').input_value()=='گردنبند'
    await page.locator('#product-filters .btn-close').click(); await page.locator('#product-filters').wait_for(state='hidden')
    await page.locator('.navbar-toggler').click(); await page.locator('.mobile-filter-trigger').click(); await page.locator('#q').wait_for(state='visible')
    await page.locator('.offcanvas-backdrop').click(position={'x':2,'y':2}); await page.locator('#product-filters').wait_for(state='hidden')
    await page.locator('.navbar-toggler').click(); await page.locator('.mobile-filter-trigger').click(); await page.locator('#q').wait_for(state='visible')
    await page.locator('#product-filters a.btn-link').click(); await page.wait_for_url(ROOT+'/')
   else: assert await page.locator('#q').is_visible()
   ids=await page.locator('[id]').evaluate_all('(es)=>es.map(e=>e.id)'); assert len(ids)==len(set(ids))
   await page.screenshot(path=f'.artifacts/mobile-home-{w}.png',full_page=True)
   for path in ['/Account/Login','/Account/Register']:
    await page.goto(ROOT+path); assert not await page.locator('.mobile-filter-trigger').count()
    assert not await page.evaluate('document.documentElement.scrollWidth>innerWidth')
   results.append({'viewport':f'{w}x{h}','result':'passed'})
  assert not errors,errors
  print(json.dumps(results,indent=2)); await browser.close()
asyncio.run(main())
