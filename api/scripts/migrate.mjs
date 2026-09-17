import fs from 'node:fs/promises'
import path from 'node:path'
import process from 'node:process'
import { fileURLToPath } from 'node:url'
import { Pool } from '@neondatabase/serverless'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const migrationsDir = path.resolve(__dirname, '../../database/migrations')
const databaseUrl = process.env.DATABASE_URL

if (!databaseUrl) {
  console.error('DATABASE_URL não configurada.')
  process.exit(1)
}

const pool = new Pool({ connectionString: databaseUrl })
const client = await pool.connect()

try {
  await client.query(`CREATE TABLE IF NOT EXISTS truckhub_schema_migrations (version VARCHAR(120) PRIMARY KEY, applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW())`)

  // Lock must be acquired before reading migration history. This prevents two deploys
  // from both observing the same pending migration at the same time.
  await client.query(`SELECT pg_advisory_lock(hashtext('truckhub-schema-migrations'))`)

  try {
    const files = (await fs.readdir(migrationsDir))
      .filter(name => /^\d+_.+\.sql$/i.test(name))
      .sort((a, b) => a.localeCompare(b, 'en'))

    const appliedResult = await client.query(`SELECT version FROM truckhub_schema_migrations`)
    const applied = new Set(appliedResult.rows.map(row => row.version))

    for (const filename of files) {
      if (applied.has(filename)) {
        console.log(`SKIP ${filename}`)
        continue
      }

      const fullPath = path.join(migrationsDir, filename)
      const contents = await fs.readFile(fullPath, 'utf8')
      console.log(`APPLY ${filename}`)

      // Run the migration and its history record in the same PostgreSQL transaction.
      // Migration SQL is trusted repository content and is intentionally executed as raw SQL.
      await client.query('BEGIN')
      try {
        await client.query(contents)
        await client.query(
          `INSERT INTO truckhub_schema_migrations (version) VALUES ($1) ON CONFLICT (version) DO NOTHING`,
          [filename]
        )
        await client.query('COMMIT')
      } catch (error) {
        await client.query('ROLLBACK')
        throw error
      }

      console.log(`OK    ${filename}`)
    }

    const rowsResult = await client.query(`SELECT version, applied_at FROM truckhub_schema_migrations ORDER BY version`)
    console.log(`Migrations aplicadas: ${rowsResult.rows.length}`)
    for (const row of rowsResult.rows) {
      console.log(` - ${row.version} (${row.applied_at instanceof Date ? row.applied_at.toISOString() : row.applied_at})`)
    }
  } finally {
    await client.query(`SELECT pg_advisory_unlock(hashtext('truckhub-schema-migrations'))`)
  }
} finally {
  await client.release()
  await pool.end()
}
