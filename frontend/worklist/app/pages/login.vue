<!--
  ======================================================
  pages/login.vue — ログインページ
  ======================================================
  dicom-tool-2/nuxt からの移植（ロジック・見た目は完全に同一）。
  Backend API（backend/DicomTool.Api、ポート5030）の login ミューテーションでhttpOnly Cookieを
  発行させ、stores/authStore.ts の状態を更新する。

  definePageMeta({ layout: 'centered' }) で、このページだけ既定レイアウトではなく
  layouts/centered.vue（画面中央寄せ）を使うよう指定している。
-->

<template>
  <div class="login-card">
    <NuxtLink to="/" class="back-link">← 検査一覧に戻る</NuxtLink>
    <h1>ログイン</h1>
    <p class="page-desc">
      開発用アカウント: <code>admin / admin1234</code>（管理者）、
      <code>dr-tanaka / doctor1234</code>（一般）
    </p>

    <form class="login-form" @submit.prevent="handleSubmit">
      <label class="field">
        <span>ユーザー名</span>
        <input v-model="username" type="text" autocomplete="username" required />
      </label>
      <label class="field">
        <span>パスワード</span>
        <input v-model="password" type="password" autocomplete="current-password" required />
      </label>

      <p v-if="store.error" class="error-msg">{{ store.error }}</p>

      <SaveButton type="submit" :disabled="store.loading" full-width>
        {{ store.loading ? 'ログイン中…' : 'ログイン' }}
      </SaveButton>
    </form>
  </div>
</template>

<script setup lang="ts">
// このページだけ既定の layouts/default.vue ではなく layouts/centered.vue を使う。
definePageMeta({
  layout: 'centered',
})

const router = useRouter()
const store = useAuthStore()

const username = ref('')
const password = ref('')

async function handleSubmit() {
  try {
    await store.login(username.value, password.value)
    router.push('/')
  } catch {
    // エラーメッセージは store.error 経由でテンプレートに表示済みなのでここでは何もしない
  }
}
</script>

<style scoped>
.login-card {
  width: 360px;
  max-width: 100%;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 10px;
  padding: 1.5rem;
}

.back-link {
  display: inline-block;
  font-size: 0.8rem;
  color: var(--color-accent);
  text-decoration: none;
  margin-bottom: 0.75rem;
}

.back-link:hover {
  text-decoration: underline;
}

.login-card h1 {
  margin: 0 0 0.5rem;
  font-size: 1.1rem;
  color: var(--color-text-heading);
}

.page-desc {
  margin: 0 0 1.25rem;
  font-size: 0.78rem;
  color: var(--color-text-muted);
  line-height: 1.6;
}

.page-desc code {
  background: var(--color-bg);
  padding: 1px 4px;
  border-radius: 3px;
  color: var(--color-accent);
}

.login-form {
  display: flex;
  flex-direction: column;
  gap: 0.9rem;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
  font-size: 0.8rem;
  color: var(--color-text-muted);
}

.field input {
  background: var(--color-bg);
  color: var(--color-text);
  border: 1px solid var(--color-border-strong);
  border-radius: 6px;
  padding: 0.5rem 0.65rem;
  font-size: 0.88rem;
}

.field input:focus {
  outline: 1px solid var(--color-accent);
}

.error-msg {
  margin: 0;
  font-size: 0.8rem;
  color: var(--color-danger);
}
</style>
