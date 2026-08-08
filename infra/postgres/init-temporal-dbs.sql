-- Temporal Server が必要とする2つのDB（本体用 / 可視性ストア用）を追加作成する。
-- POSTGRES_DB(=dicomtool)はpostgresイメージが自動作成するが、それ以外は自前で作る必要がある。
-- このファイルは docker-entrypoint-initdb.d 経由で「コンテナ初回起動時のみ」実行される。
CREATE DATABASE temporal;
CREATE DATABASE temporal_visibility;
