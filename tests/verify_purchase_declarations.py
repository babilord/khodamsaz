"""Run against a fresh, isolated Development database with demo seeding enabled."""

import asyncio
import os
import sys
from uuid import uuid4

from playwright.async_api import async_playwright, expect

ROOT = (sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5278").rstrip("/")
ENDPOINT = ROOT + "/PurchaseDeclarations/Create"
TOKEN = "input[name=__RequestVerificationToken]"


async def login(page, email, path="/Account/Login"):
    await page.goto(ROOT + path)
    await page.locator("#Email").fill(email)
    await page.locator("#Password").fill(os.environ["Seed__DemoPassword"])
    await page.locator("form").filter(has=page.locator("#Email")).get_by_role("button", name="ورود", exact=True).click()
    await expect(page.locator("#logout-form")).to_be_attached()


async def submit(page, product, key=None, token=None):
    return await page.request.post(ENDPOINT, form={
        "productId": product,
        "requestId": key or str(uuid4()),
        "__RequestVerificationToken": token or await page.locator(TOKEN).first.input_value(),
    }, max_redirects=0)


async def counts(response, status, buyers, total, remaining):
    assert response.status == status, (response.status, await response.text())
    payload = await response.json()
    assert set(payload) == {"uniqueBuyers", "total", "remaining", "message"}, payload
    assert (payload["uniqueBuyers"], payload["total"], payload["remaining"]) == (buyers, total, remaining)


async def main():
    async with async_playwright() as playwright:
        browser = await playwright.chromium.launch(channel=os.getenv("BROWSER_CHANNEL", "msedge"))
        buyer = await browser.new_page(viewport={"width": 320, "height": 740})
        await buyer.goto(ROOT + "/")
        cards = buyer.locator(".listing-card")
        assert await cards.count() >= 3
        assert not await buyer.evaluate("document.documentElement.scrollWidth > innerWidth")
        assert await cards.evaluate_all("(cards) => cards.every(c => c.scrollWidth === c.clientWidth)")
        # Seeded products 1 and 3 share the same maker.
        product, other = 1, 3
        path = f"/Listings/Details/{product}"
        anonymous_link = cards.locator(f'[data-purchase-product="{product}"] a')
        login_url = await anonymous_link.get_attribute("href")
        assert path in login_url or "%2FListings%2FDetails%2F1" in login_url
        anonymous_post = await buyer.request.post(ENDPOINT, form={"productId": product}, max_redirects=0)
        assert anonymous_post.status == 302 and "/account/access" in anonymous_post.headers["location"].lower()
        await login(buyer, "ali@handmade.local", login_url)
        assert buyer.url == ROOT + path

        modal = buyer.locator("#purchase-declaration-modal")
        trigger = buyer.locator("[data-purchase-open]")
        stats = buyer.locator("[data-purchase-stats]")
        confirm = modal.locator("[data-purchase-confirm]")
        await expect(stats).to_contain_text("۰ نفر، ۰ بار")
        await trigger.click()
        await expect(modal).to_be_visible()
        await expect(modal).to_have_attribute("dir", "rtl")
        await expect(modal.locator("h2")).to_have_text("آیا مطمئنی که این محصول را خریدی؟")
        await modal.get_by_text("انصراف", exact=True).click()
        await expect(modal).to_be_hidden()
        await buyer.reload()
        await expect(stats).to_contain_text("۰ نفر، ۰ بار")

        # Drop the first response after it commits, then confirm again with the same operation ID.
        async def lose_response(route):
            result = await route.fetch()
            assert result.status == 200
            await route.abort()

        await buyer.route(ENDPOINT, lose_response, times=1)
        await trigger.click()
        await confirm.click()
        await expect(modal.locator("[data-purchase-error]")).to_be_visible()
        await confirm.evaluate("(button) => { button.click(); button.click(); }")
        await expect(modal).to_be_hidden()
        await expect(stats).to_contain_text("۱ نفر، ۱ بار")
        await expect(buyer.locator("[data-purchase-success]")).to_have_text("خرید شما با موفقیت ثبت شد.")
        assert buyer.url == ROOT + path
        await expect(buyer.locator("[data-purchase-allowance]")).to_contain_text("۹ بار")

        await buyer.goto(ROOT + "/")
        card = cards.locator(f'[data-purchase-product="{product}"]')
        await card.locator("[data-purchase-open]").click()
        await confirm.click()
        await expect(card.locator("[data-purchase-stats]")).to_contain_text("۱ نفر، ۲ بار")
        await expect(modal).to_be_hidden()
        await buyer.goto(ROOT + path)
        assert (await buyer.request.post(ENDPOINT, form={"productId": product, "requestId": str(uuid4())})).status == 400
        assert (await submit(buyer, 999999)).status == 404
        assert (await submit(buyer, product, "invalid")).status == 400

        owner = await browser.new_page()
        await login(owner, "sara@handmade.local")
        await owner.goto(ROOT + path)
        assert await owner.locator("[data-purchase-open]").count() == 0
        assert (await submit(owner, product)).status == 403

        second = await browser.new_page()
        await login(second, "admin@handmade.local")
        await second.goto(ROOT + path)
        await counts(await submit(second, product), 200, 2, 3, 9)
        await counts(await submit(buyer, other), 200, 1, 1, 9)

        key = str(uuid4())
        token = await buyer.locator(TOKEN).first.input_value()
        repeats = await asyncio.gather(*(submit(buyer, product, key, token) for _ in range(4)))
        for response in repeats:
            await counts(response, 200, 2, 4, 7)
        for count in range(4, 10):
            await counts(await submit(buyer, product), 200, 2, count + 1, 10 - count)
        parallel = await asyncio.gather(*(submit(buyer, product, token=token) for _ in range(8)))
        assert sorted(response.status for response in parallel) == [200] + [409] * 7
        for response in parallel:
            await counts(response, response.status, 2, 11, 0)
        await counts(await submit(buyer, product, key), 200, 2, 11, 0)
        await counts(await submit(buyer, product), 409, 2, 11, 0)
        await buyer.close()

        new_session = await browser.new_page()
        await login(new_session, "ali@handmade.local", login_url)
        await expect(new_session.locator("[data-purchase-open]")).to_be_disabled()
        await expect(new_session.locator("[data-purchase-allowance]")).to_contain_text("حداکثر ۱۰ بار")
        await counts(await submit(new_session, product), 409, 2, 11, 0)
        await counts(await submit(new_session, other), 200, 1, 2, 8)

        public = await browser.new_page()
        for url in [ROOT + path, ROOT + "/"]:
            await public.goto(url)
            section = public.locator(f'[data-purchase-product="{product}"]')
            await expect(section.locator("[data-purchase-stats]")).to_contain_text("۲ نفر، ۱۱ بار")
            assert await section.locator("[data-purchase-allowance]").count() == 0
            assert await section.locator("form").count() == 0
            markup = await public.content()
            assert all(identity not in markup for identity in (
                "ali@handmade.local", "admin@handmade.local", "علی محمدی", "مدیر بازار",
            ))
        await browser.close()
        print("PASS: purchase declaration browser, HTTP, privacy, idempotency, and concurrency checks")


asyncio.run(main())
